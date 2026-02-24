using Sarifintown.Services;

namespace Sarifintown.Helpers
{
    public static class FileHelper
    {
        private enum MatchTier
        {
            Flexible = 1, // weakest heuristic
            Direct = 2, // strong direct anchor
            Wrapper = 3  // strongest wrapper/duplicate correction
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                path = new Uri(path).LocalPath;

            path = path.Replace('\\', '/');
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();

            foreach (var part in parts)
            {
                if (part == "..")
                {
                    if (stack.Count > 0) stack.Pop();
                }
                else if (part != ".")
                {
                    stack.Push(part);
                }
            }

            return string.Join("/", stack.Reverse());
        }

        public static (string adjustedPath, DirectoryPicker matchedFolder) AdjustPathToGrantedFolder(
            string normalizedSarifPath,
            IEnumerable<DirectoryPicker> accessibleFolders,
            out string error)
        {
            error = null;
            var pathSegments = normalizedSarifPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            (string adjusted, DirectoryPicker folder, int score)? best = null;

            foreach (var folder in accessibleFolders ?? Enumerable.Empty<DirectoryPicker>())
            {
                if (string.IsNullOrWhiteSpace(folder?.Name))
                    continue;

                var name = folder.Name.Trim('/');

                // Normalize subdirectories to "<name>/..."
                var normalizedSubs = (folder.Subdirectories ?? Enumerable.Empty<string>())
                    .Select(s => (s ?? string.Empty).Trim('/'))
                    .Where(s => s.Length > 0)
                    .Select(s => s.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase) ? s : $"{name}/{s}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var immediateChildren = normalizedSubs
                    .Select(s => s.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    .Where(segs => segs.Length >= 2 && segs[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Select(segs => segs[1])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Case: "parent/child" → just "child"
                if (pathSegments.Length >= 2 &&
                    pathSegments[0].Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    immediateChildren.Contains(pathSegments[1], StringComparer.OrdinalIgnoreCase))
                {
                    var rest = string.Join("/", pathSegments.Skip(1));
                    AddCandidate(ref best, rest, folder, MatchTier.Direct, bonus: 50);
                }

                // Case: Direct anchor on the right-most "<name>"
                int idx = LastIndexOfSegment(pathSegments, name);
                if (idx >= 0)
                {
                    var tail = pathSegments.Skip(idx + 1).ToArray();

                    bool duplicateBefore = idx > 0 && pathSegments[idx - 1].Equals(name, StringComparison.OrdinalIgnoreCase);
                    if (duplicateBefore && immediateChildren.Count == 1)
                    {
                        var child = immediateChildren[0];
                        string adjusted = tail.Length > 0
                            ? $"{name}/{child}/{string.Join("/", tail)}"
                            : $"{name}/{child}";
                        AddCandidate(ref best, adjusted, folder, MatchTier.Wrapper, bonus: idx + 100);
                    }
                    else
                    {
                        // Drop root if adjusted would equal only the folder
                        string adjusted = tail.Length > 0
                            ? $"{name}/{string.Join("/", tail)}"
                            : string.Join("/", pathSegments.Skip(idx + 1));

                        AddCandidate(ref best, adjusted, folder, MatchTier.Direct, bonus: idx);
                    }
                }

                // Case: Flexible subdir match
                var flex = BestSubdirFlexibleMatch(normalizedSubs, pathSegments, name);
                if (flex != null)
                {
                    var rest = string.Join("/", pathSegments.Skip(flex.MatchedLength));
                    string adjusted = string.IsNullOrEmpty(rest) ? flex.Subdir : $"{flex.Subdir}/{rest}";

                    // Drop the root (granted folder) segment
                    var segs = adjusted.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segs.Length > 1 && segs[0].Equals(folder.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        adjusted = string.Join("/", segs.Skip(1));
                    }

                    int bonus = flex.MatchedLength * 10 + flex.SubdirDepth;
                    AddCandidate(ref best, adjusted, folder, MatchTier.Flexible, bonus);
                }
            }

            if (best != null)
            {
                return (best.Value.adjusted, best.Value.folder);
            }

            error = "The file path could not be reconciled with any granted folder.";
            return (null, null);
        }

        private static void AddCandidate(
            ref (string adjusted, DirectoryPicker folder, int score)? best,
            string adjusted,
            DirectoryPicker folder,
            MatchTier tier,
            int bonus = 0)
        {
            if (string.IsNullOrEmpty(adjusted))
                return;

            // Ensure we never return just the granted folder root
            var segs = adjusted.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length == 1 && folder.Name.Equals(segs[0], StringComparison.OrdinalIgnoreCase))
            {
                adjusted = string.Join("/", segs.Skip(1)); // drop root
                if (string.IsNullOrEmpty(adjusted))
                    return;
            }

            int score = (int)tier * 1000 + bonus;
            if (best == null || score > best.Value.score)
            {
                best = (adjusted, folder, score);
            }
        }

        private static int LastIndexOfSegment(string[] segments, string value)
        {
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                if (segments[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private sealed class FlexMatch
        {
            public string Subdir { get; init; }
            public int MatchedLength { get; init; }
            public int SubdirDepth { get; init; }
        }

        private static FlexMatch BestSubdirFlexibleMatch(string[] normalizedSubdirs, string[] pathSegments, string rootName)
        {
            FlexMatch best = null;

            foreach (var sub in normalizedSubdirs)
            {
                var segs = sub.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length == 0 || !segs[0].Equals(rootName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var subTail = segs.Skip(1).ToArray();

                for (int offset = 0; offset < subTail.Length; offset++)
                {
                    int matchLen = 0;
                    int max = Math.Min(pathSegments.Length, subTail.Length - offset);
                    while (matchLen < max &&
                           pathSegments[matchLen].Equals(subTail[offset + matchLen], StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen++;
                    }

                    if (matchLen > 0 &&
                        (best == null ||
                         matchLen > best.MatchedLength ||
                         (matchLen == best.MatchedLength && segs.Length > best.SubdirDepth)))
                    {
                        best = new FlexMatch
                        {
                            Subdir = string.Join("/", segs),
                            MatchedLength = matchLen,
                            SubdirDepth = segs.Length
                        };
                    }
                }
            }

            return best;
        }
    }
}
