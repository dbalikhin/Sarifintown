using Sarifintown.Models;

namespace Sarifintown.AgentEngine.Sync;

internal sealed class GhasSyncProvider : IUpstreamSyncProvider
{
    public string ProviderName => "GitHubAdvancedSecurity";

    public bool CanHandle(string toolDriverName)
    {
        if (string.IsNullOrWhiteSpace(toolDriverName))
        {
            return false;
        }

        var normalized = toolDriverName.Trim().ToLowerInvariant();
        return normalized.Contains("codeql", StringComparison.Ordinal)
               || normalized.Contains("github", StringComparison.Ordinal);
    }

    public Task<SyncOperationResult> SyncTriageAsync(
        LedgerEntry entry,
        Result originalSarifResult,
        SyncContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(originalSarifResult);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.ApiToken))
        {
            return Task.FromResult(new SyncOperationResult(false, UpstreamSyncStatus.Failed, "GHAS_TOKEN (or GITHUB_TOKEN) environment variable is not set."));
        }

        return Task.FromResult(new SyncOperationResult(false, UpstreamSyncStatus.Failed, "GitHub Advanced Security sync provider is not implemented yet."));
    }
}
