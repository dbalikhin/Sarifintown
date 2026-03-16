using Sarifintown.AgentEngine.Configuration;
using Sarifintown.Models;

namespace Sarifintown.AgentEngine.Sync;

internal interface IUpstreamSyncProvider
{
    string ProviderName { get; }

    bool CanHandle(string toolDriverName);

    Task<SyncOperationResult> SyncTriageAsync(
        LedgerEntry entry,
        Result originalSarifResult,
        SyncOptions options,
        CancellationToken cancellationToken);
}

internal sealed record SyncOperationResult(
    bool IsSuccess,
    UpstreamSyncStatus Status,
    string? ErrorMessage,
    bool ShouldAbortBatch = false);
