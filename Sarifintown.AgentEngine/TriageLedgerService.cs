using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sarifintown.AgentEngine;

/// <summary>
/// Thread-safe service for reading and writing the triage ledger (.sarif/triage-ledger.json).
/// </summary>
internal sealed class TriageLedgerService : IDisposable
{
    private const string LedgerFileName = "triage-ledger.json";

    private static readonly JsonSerializerOptions LedgerJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _workspaceRoot;
    private TriageLedgerDocument? _cached;

    internal TriageLedgerService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// Loads the current ledger from disk, returning a cached copy if already loaded.
    /// </summary>
    internal async Task<TriageLedgerDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached != null)
            {
                return _cached;
            }

            _cached = await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Upserts one or more entries into the ledger and persists to disk.
    /// </summary>
    internal async Task UpsertAsync(
        IReadOnlyList<(string CompositeKey, LedgerEntry Entry)> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cached ??= await LoadFromDiskAsync(cancellationToken).ConfigureAwait(false);

            foreach (var (key, entry) in items)
            {
                _cached.Entries[key] = entry;
            }

            await SaveToDiskAsync(_cached, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns all entries matching the given upstream sync status.
    /// </summary>
    internal async Task<IReadOnlyList<(string CompositeKey, LedgerEntry Entry)>> GetBySyncStatusAsync(
        UpstreamSyncStatus status,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);

        return document.Entries
            .Where(kvp => kvp.Value.UpstreamSync.Status == status)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    /// <summary>
    /// Returns entries matching the specified composite keys.
    /// </summary>
    internal async Task<IReadOnlyList<(string CompositeKey, LedgerEntry Entry)>> GetByKeysAsync(
        IEnumerable<string> compositeKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compositeKeys);

        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var keySet = compositeKeys.ToHashSet(StringComparer.Ordinal);

        return document.Entries
            .Where(kvp => keySet.Contains(kvp.Key))
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    /// <summary>
    /// Invalidates the in-memory cache, forcing the next load to read from disk.
    /// </summary>
    internal void InvalidateCache()
    {
        _lock.Wait();
        try
        {
            _cached = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private string GetLedgerFilePath()
    {
        return Path.Combine(_workspaceRoot, ".sarif", LedgerFileName);
    }

    private async Task<TriageLedgerDocument> LoadFromDiskAsync(CancellationToken cancellationToken)
    {
        var path = GetLedgerFilePath();

        if (!File.Exists(path))
        {
            return new TriageLedgerDocument();
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TriageLedgerDocument();
        }

        try
        {
            return JsonSerializer.Deserialize<TriageLedgerDocument>(json, LedgerJsonOptions) ?? new TriageLedgerDocument();
        }
        catch (JsonException)
        {
            return new TriageLedgerDocument();
        }
    }

    private async Task SaveToDiskAsync(TriageLedgerDocument document, CancellationToken cancellationToken)
    {
        var path = GetLedgerFilePath();
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(document, LedgerJsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
