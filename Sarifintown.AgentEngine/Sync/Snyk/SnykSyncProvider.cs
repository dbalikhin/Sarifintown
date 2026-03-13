using Sarifintown.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Sarifintown.AgentEngine.Sync.Snyk;

internal sealed class SnykSyncProvider : IUpstreamSyncProvider
{
    private const string AssetFingerprintKey = "snyk/asset/finding/v1";
    private const int MaxRateLimitRetries = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;

    internal SnykSyncProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public string ProviderName => "Snyk";

    public bool CanHandle(string toolDriverName)
    {
        if (string.IsNullOrWhiteSpace(toolDriverName))
        {
            return false;
        }

        var normalized = toolDriverName.Trim().ToLowerInvariant();
        return normalized.Contains("snyk", StringComparison.Ordinal);
    }

    public async Task<SyncOperationResult> SyncTriageAsync(
        LedgerEntry entry,
        Result originalSarifResult,
        SyncContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(originalSarifResult);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryMapDecisionToReasonType(entry.TriageDecision.State, out var reasonType, out var reasonPrefix))
        {
            return new SyncOperationResult(true, UpstreamSyncStatus.Synced, null);
        }

        if (string.IsNullOrWhiteSpace(context.ApiToken))
        {
            return new SyncOperationResult(false, UpstreamSyncStatus.Failed, "SNYK_TOKEN environment variable is not set.", ShouldAbortBatch: true);
        }

        if (string.IsNullOrWhiteSpace(context.OrganizationId))
        {
            return new SyncOperationResult(false, UpstreamSyncStatus.Failed, "SNYK_ORG_ID environment variable is not set.");
        }

        var fingerprints = originalSarifResult.Fingerprints;
        if (fingerprints == null || !fingerprints.TryGetValue(AssetFingerprintKey, out var issueId) || string.IsNullOrWhiteSpace(issueId))
        {
            return new SyncOperationResult(
                false,
                UpstreamSyncStatus.Failed,
                "Missing 'snyk/asset/finding/v1' fingerprint. Consistent ignores requires this asset identifier.");
        }

        var payload = new SnykIgnorePayload
        {
            Data = new SnykIgnoreData
            {
                Attributes = new SnykIgnoreAttributes
                {
                    ReasonType = reasonType,
                    Reason = BuildReason(entry, reasonPrefix)
                },
                Relationships = new SnykIgnoreRelationships
                {
                    Issue = new SnykIssueRelationship
                    {
                        Data = new SnykIssueData
                        {
                            Id = issueId
                        }
                    }
                }
            }
        };

        var url = $"https://api.snyk.io/rest/orgs/{Uri.EscapeDataString(context.OrganizationId)}/ignores?version=2024-10-15~beta";

        for (var attempt = 0; attempt <= MaxRateLimitRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload, new MediaTypeHeaderValue("application/vnd.api+json"))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("TOKEN", context.ApiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;

            if (statusCode == HttpStatusCode.Created)
            {
                return new SyncOperationResult(true, UpstreamSyncStatus.Synced, null);
            }

            if (statusCode == HttpStatusCode.Conflict)
            {
                return new SyncOperationResult(true, UpstreamSyncStatus.Synced, "Already ignored upstream");
            }

            if (statusCode == HttpStatusCode.NotFound)
            {
                return new SyncOperationResult(false, UpstreamSyncStatus.Failed, "Target finding not found (likely resolved in code).");
            }

            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
            {
                return new SyncOperationResult(false, UpstreamSyncStatus.Failed, "Auth/Permissions failed.", ShouldAbortBatch: true);
            }

            if (statusCode == (HttpStatusCode)429)
            {
                if (attempt == MaxRateLimitRetries)
                {
                    return new SyncOperationResult(false, UpstreamSyncStatus.Pending, "Rate limited.");
                }

                var retryDelay = ResolveRetryDelay(response.Headers.RetryAfter);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (statusCode == HttpStatusCode.BadRequest)
            {
                var badRequestMessage = await TryReadSnykErrorDetailAsync(response, cancellationToken).ConfigureAwait(false)
                    ?? "Snyk request validation failed.";
                return new SyncOperationResult(false, UpstreamSyncStatus.Failed, badRequestMessage);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(error))
            {
                error = $"Unexpected Snyk response: {(int)response.StatusCode} {response.ReasonPhrase}";
            }

            return new SyncOperationResult(false, UpstreamSyncStatus.Failed, error);
        }

        return new SyncOperationResult(false, UpstreamSyncStatus.Pending, "Rate limited.");
    }

    internal static bool TryMapDecisionToReasonType(TriageDecisionState decisionState, out string reasonType, out string reasonPrefix)
    {
        reasonType = string.Empty;
        reasonPrefix = string.Empty;

        switch (decisionState)
        {
            case TriageDecisionState.FalsePositive:
                reasonType = "not-vulnerable";
                return true;
            case TriageDecisionState.WontFix:
                reasonType = "wont-fix";
                return true;
            case TriageDecisionState.Mitigated:
                reasonType = "temporary-ignore";
                return true;
            case TriageDecisionState.TestCode:
                reasonType = "not-vulnerable";
                reasonPrefix = "[Test Code] ";
                return true;
            case TriageDecisionState.Confirmed:
                return false;
            default:
                return false;
        }
    }

    private static string BuildReason(LedgerEntry entry, string reasonPrefix)
    {
        var reason = entry.TriageDecision.ShortReason;
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "Updated by sarif_sync";
        }

        return string.IsNullOrWhiteSpace(reasonPrefix)
            ? reason
            : string.Concat(reasonPrefix, reason);
    }

    private static TimeSpan ResolveRetryDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } retryDate)
        {
            var dateDelay = retryDate - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
            {
                return dateDelay;
            }
        }

        return DefaultRetryDelay;
    }

    private static async Task<string?> TryReadSnykErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<SnykErrorResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return error?.Errors.FirstOrDefault()?.Detail;
        }
        catch
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
