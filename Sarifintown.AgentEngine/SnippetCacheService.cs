using System.Collections.Concurrent;

namespace Sarifintown.AgentEngine;

internal sealed class SnippetCacheService
{
    private readonly ConcurrentDictionary<string, string> _cache = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    internal bool TryGet(string key, out string snippet)
    {
        return _cache.TryGetValue(key, out snippet!);
    }

    internal void Set(string key, string snippet)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(snippet))
        {
            return;
        }

        _cache[key] = snippet;
    }
}
