using Sarifintown.Models;

namespace Sarifintown.AgentEngine.Sync;

internal interface IUpstreamSyncProvider
{
    string ProviderName { get; }

    bool CanHandle(string toolDriverName);

    Task<SyncOperationResult> SyncTriageAsync(
        LedgerEntry entry,
        Result originalSarifResult,
        SyncContext context,
        CancellationToken cancellationToken);
}

internal sealed record SyncContext(string ApiToken, string OrganizationId);

internal sealed record SyncOperationResult(
    bool IsSuccess,
    UpstreamSyncStatus Status,
    string? ErrorMessage,
    bool ShouldAbortBatch = false);
