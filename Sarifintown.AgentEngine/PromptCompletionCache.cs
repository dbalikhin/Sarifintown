using System.Text.Json;

namespace Sarifintown.AgentEngine;

internal sealed record PromptCompletionData(
    IReadOnlyList<string> Severities,
    IReadOnlyList<string> Rules,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> FindingIds,
    IReadOnlyList<string> ListStates,
    IReadOnlyList<string> DecisionStates,
    IReadOnlyList<string> EvidenceModes,
    IReadOnlyList<string> DryRunValues,
    IReadOnlyList<string> Limits,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Authors);

internal sealed class PromptCompletionCache
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly string _workspaceRoot;
    private readonly IReadOnlyList<string> _sarifFiles;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private PromptCompletionData? _cachedData;
    private DateTimeOffset _cachedAtUtc = DateTimeOffset.MinValue;

    internal PromptCompletionCache(string workspaceRoot, IEnumerable<string> sarifFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(sarifFiles);

        _workspaceRoot = workspaceRoot;
        _sarifFiles = sarifFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal async Task<PromptCompletionData> GetAsync(CancellationToken cancellationToken)
    {
        if (_cachedData is not null && DateTimeOffset.UtcNow - _cachedAtUtc < CacheLifetime)
        {
            return _cachedData;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedData is not null && DateTimeOffset.UtcNow - _cachedAtUtc < CacheLifetime)
            {
                return _cachedData;
            }

            _cachedData = await LoadDataAsync(cancellationToken);
            _cachedAtUtc = DateTimeOffset.UtcNow;
            return _cachedData;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<PromptCompletionData> LoadDataAsync(CancellationToken cancellationToken)
    {
        var severities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "error",
            "warning",
            "note",
            "none",
            "high",
            "medium",
            "low",
            "critical"
        };

        var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var findingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Confirmed true positive",
            "False positive - acceptable pattern",
            "Needs manual review"
        };
        var authors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AI"
        };

        foreach (var sarifFile in _sarifFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(sarifFile))
            {
                continue;
            }

            await using var stream = File.OpenRead(sarifFile);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!TryGetPropertyInsensitive(document.RootElement, "runs", out var runsElement) || runsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var runElement in runsElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryGetPropertyInsensitive(runElement, "results", out var resultsElement) || resultsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var resultElement in resultsElement.EnumerateArray())
                {
                    if (TryGetPropertyInsensitive(resultElement, "resultIdentity", out var findingIdElement)
                        && findingIdElement.ValueKind == JsonValueKind.String)
                    {
                        var findingId = findingIdElement.GetString();
                        if (!string.IsNullOrWhiteSpace(findingId))
                        {
                            findingIds.Add(findingId.Trim());
                        }
                    }

                    if (TryGetPropertyInsensitive(resultElement, "ruleId", out var ruleElement)
                        && ruleElement.ValueKind == JsonValueKind.String)
                    {
                        var rule = ruleElement.GetString();
                        if (!string.IsNullOrWhiteSpace(rule))
                        {
                            rules.Add(rule.Trim());
                        }
                    }

                    if (TryGetPropertyInsensitive(resultElement, "level", out var levelElement)
                        && levelElement.ValueKind == JsonValueKind.String)
                    {
                        var level = levelElement.GetString();
                        if (!string.IsNullOrWhiteSpace(level))
                        {
                            severities.Add(level.Trim());
                        }
                    }

                    if (TryGetPropertyInsensitive(resultElement, "locations", out var locationsElement)
                        && locationsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var locationElement in locationsElement.EnumerateArray())
                        {
                            if (!TryGetPropertyInsensitive(locationElement, "physicalLocation", out var physicalLocationElement)
                                || !TryGetPropertyInsensitive(physicalLocationElement, "artifactLocation", out var artifactLocationElement)
                                || !TryGetPropertyInsensitive(artifactLocationElement, "uri", out var uriElement)
                                || uriElement.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var uri = uriElement.GetString();
                            if (!string.IsNullOrWhiteSpace(uri))
                            {
                                files.Add(uri.Trim());
                            }
                        }
                    }
                }
            }
        }

        var triagePath = Path.Combine(_workspaceRoot, ".sarif", "triage.json");
        if (File.Exists(triagePath))
        {
            await using var triageStream = File.OpenRead(triagePath);
            using var triageDocument = await JsonDocument.ParseAsync(triageStream, cancellationToken: cancellationToken);

            if (TryGetPropertyInsensitive(triageDocument.RootElement, "entries", out var entriesElement)
                && entriesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entryElement in entriesElement.EnumerateArray())
                {
                    if (TryGetPropertyInsensitive(entryElement, "findingId", out var triageFindingIdElement)
                        && triageFindingIdElement.ValueKind == JsonValueKind.String)
                    {
                        var findingId = triageFindingIdElement.GetString();
                        if (!string.IsNullOrWhiteSpace(findingId))
                        {
                            findingIds.Add(findingId.Trim());
                        }
                    }

                    if (TryGetPropertyInsensitive(entryElement, "reason", out var reasonElement)
                        && reasonElement.ValueKind == JsonValueKind.String)
                    {
                        var reason = reasonElement.GetString();
                        if (!string.IsNullOrWhiteSpace(reason))
                        {
                            reasons.Add(reason.Trim());
                        }
                    }

                    if (TryGetPropertyInsensitive(entryElement, "author", out var authorElement)
                        && authorElement.ValueKind == JsonValueKind.String)
                    {
                        var author = authorElement.GetString();
                        if (!string.IsNullOrWhiteSpace(author))
                        {
                            authors.Add(author.Trim());
                        }
                    }
                }
            }
        }

        return new PromptCompletionData(
            Severities: severities.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Rules: rules.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Files: files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            FindingIds: findingIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ListStates: new[] { "open", "tp", "fp" },
            DecisionStates: new[] { "tp", "fp" },
            EvidenceModes: new[] { "line-window-strict", "line-window-concatenated", "tree-sitter-method" },
            DryRunValues: new[] { "false", "true" },
            Limits: new[] { "10", "25", "50", "100" },
            Reasons: reasons.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Authors: authors.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool TryGetPropertyInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
