using Sarifintown.AgentEngine.Configuration;
using Sarifintown.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sarifintown.AgentEngine.Sync.Snyk;

internal sealed class SnykSyncProvider : IUpstreamSyncProvider
{
    private const string AssetFingerprintKey = "snyk/asset/finding/v1";
    private const int MaxRateLimitRetries = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Strips local finding ID prefixes (e.g., "Finding 5:", "#3:", "Index 2:") that have no meaning upstream.
    /// </summary>
    private static readonly Regex LocalIdPrefixPattern = new(
        @"^(Finding\s*#?\d+[:\s\-]+|#\d+[:\s\-]+|Index\s*\d+[:\s\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(originalSarifResult);
        ArgumentNullException.ThrowIfNull(options);

        if (HasAcceptedSuppression(originalSarifResult))
        {
            return new SyncOperationResult(true, UpstreamSyncStatus.Skipped, "Already suppressed in SARIF; no Snyk API call needed.");
        }

        if (!TryMapDecisionToReasonType(entry.TriageDecision.State, out var reasonType, out var reasonPrefix))
        {
            var skipReason = entry.TriageDecision.State == TriageDecisionState.Confirmed
                ? "Snyk does not support syncing true positives; only FP/WontFix/Mitigated/TestCode can be pushed upstream."
                : $"Triage state '{entry.TriageDecision.State}' is not supported by the Snyk ignore API.";
            return new SyncOperationResult(true, UpstreamSyncStatus.Skipped, skipReason);
        }

        if (string.IsNullOrWhiteSpace(options.SnykToken)
            || string.IsNullOrWhiteSpace(options.SnykOrgId))
        {
            return new SyncOperationResult(false, UpstreamSyncStatus.Failed, "Sync:SnykToken and Sync:SnykOrgId must be configured.", ShouldAbortBatch: true);
        }

        var issueId = ResolveSnykTargetId(entry, originalSarifResult);
        if (string.IsNullOrWhiteSpace(issueId))
        {
            return new SyncOperationResult(
                false,
                UpstreamSyncStatus.Failed,
                "Could not resolve a valid target Snyk issue ID to construct the policy condition.");
        }

        var payload = new SnykPolicyPayload
        {
            Data = new SnykPolicyData
            {
                Attributes = new SnykPolicyAttributes
                {
                    Name = $"Triage Sync: {issueId}",
                    Action = new SnykPolicyAction
                    {
                        Data = new SnykPolicyActionData
                        {
                            IgnoreType = reasonType,
                            Reason = BuildReason(entry, reasonPrefix)
                        }
                    },
                    ConditionsGroup = new SnykPolicyConditionsGroup
                    {
                        Conditions = new List<SnykPolicyCondition>
                        {
                            new()
                            {
                                Field = AssetFingerprintKey,
                                Operator = "includes",
                                Value = issueId
                            }
                        }
                    }
                }
            }
        };

        var isUpdate = !string.IsNullOrWhiteSpace(entry.Metadata.SnykPolicyId);
        var endpointUrl = isUpdate
            ? $"https://api.snyk.io/rest/orgs/{Uri.EscapeDataString(options.SnykOrgId)}/policies/{Uri.EscapeDataString(entry.Metadata.SnykPolicyId)}?version=2025-11-05"
            : $"https://api.snyk.io/rest/orgs/{Uri.EscapeDataString(options.SnykOrgId)}/policies?version=2025-11-05";
        var httpMethod = isUpdate ? HttpMethod.Patch : HttpMethod.Post;

        for (var attempt = 0; attempt <= MaxRateLimitRetries; attempt++)
        {
            using var request = new HttpRequestMessage(httpMethod, endpointUrl)
            {
                Content = JsonContent.Create(payload, new MediaTypeHeaderValue("application/vnd.api+json"))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("TOKEN", options.SnykToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;

            if (statusCode == HttpStatusCode.Created || statusCode == HttpStatusCode.OK)
            {
                if (!isUpdate && statusCode == HttpStatusCode.Created)
                {
                    var responseData = await response.Content.ReadFromJsonAsync<SnykPolicyResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
                    var policyId = responseData?.Data?.Id;
                    if (!string.IsNullOrWhiteSpace(policyId))
                    {
                        var updatedMetadata = entry.Metadata with { SnykPolicyId = policyId };
                        return new SyncOperationResult(true, UpstreamSyncStatus.Synced, null, UpdatedMetadata: updatedMetadata);
                    }
                }

                return new SyncOperationResult(true, UpstreamSyncStatus.Synced, null);
            }

            if (statusCode == HttpStatusCode.Conflict)
            {
                return new SyncOperationResult(true, UpstreamSyncStatus.Synced, "HTTP 409 Conflict: Policy already exists upstream.");
            }

            if (statusCode == HttpStatusCode.NotFound)
            {
                return new SyncOperationResult(false, UpstreamSyncStatus.Failed, "HTTP 404: Target finding not found.");
            }

            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
            {
                return new SyncOperationResult(false, UpstreamSyncStatus.Failed, "HTTP 401/403: Auth failed.", ShouldAbortBatch: true);
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
                return new SyncOperationResult(false, UpstreamSyncStatus.Failed, $"HTTP 400: {badRequestMessage}");
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(error))
            {
                error = $"Unexpected Snyk response: {(int)response.StatusCode} {response.ReasonPhrase}";
            }

            return new SyncOperationResult(false, UpstreamSyncStatus.Failed, $"HTTP {(int)response.StatusCode}: {error}");
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

    private static string? ResolveSnykTargetId(LedgerEntry entry, Result originalSarifResult)
    {
        var fingerprints = originalSarifResult.Fingerprints;

        if (fingerprints != null && fingerprints.TryGetValue(AssetFingerprintKey, out var assetId) && !string.IsNullOrWhiteSpace(assetId))
        {
            return assetId;
        }

        if (fingerprints != null && fingerprints.TryGetValue("snyk/org/project/finding/v1", out var orgId) && !string.IsNullOrWhiteSpace(orgId))
        {
            return orgId;
        }

        return entry.FindingId;
    }

    private static string BuildReason(LedgerEntry entry, string reasonPrefix)
    {
        var reason = SanitizeReason(entry.TriageDecision.ShortReason);
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "Updated by sarif_sync";
        }

        return string.IsNullOrWhiteSpace(reasonPrefix)
            ? reason
            : string.Concat(reasonPrefix, reason);
    }

    /// <summary>
    /// Strips local session-scoped identifiers that should not be sent to upstream APIs.
    /// </summary>
    internal static string SanitizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return reason;
        }

        return LocalIdPrefixPattern.Replace(reason, string.Empty).Trim();
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

    private static bool HasAcceptedSuppression(Result originalSarifResult)
    {
        return originalSarifResult.Suppressions != null
               && originalSarifResult.Suppressions.Any(s => string.Equals(s.Status, "accepted", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> TryReadSnykErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<SnykErrorResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return error?.Errors.FirstOrDefault()?.Detail;
        }
        catch (JsonException)
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
