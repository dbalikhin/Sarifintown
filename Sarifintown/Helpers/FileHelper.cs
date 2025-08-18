using Sarifintown.Services;

namespace Sarifintown.Helpers
{
    public static class FileHelper
    {
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                path = new Uri(path).LocalPath;
            }

            path = path.Replace('\\', '/');
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();

            foreach (var part in parts)
            {
                if (part == "..")
                {
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }
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
            var pathSegments = normalizedSarifPath.Split('/');
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

                // ---- NEW: Handle "parent-folder/child" → just "child" case ----
                // Example: path "SharpSecrets/secret-synth", granted folder "SharpSecrets",
                // but user wants "secret-synth" as the root.
                if (pathSegments.Length >= 2 &&
                    pathSegments[0].Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    immediateChildren.Contains(pathSegments[1], StringComparer.OrdinalIgnoreCase))
                {
                    var rest = string.Join("/", pathSegments.Skip(1));
                    AddCandidate(ref best, rest, folder, score: 1200);
                }

                // ---- Phase 1: Direct anchor on the right-most "<name>" ----
                int idx = LastIndexOfSegment(pathSegments, name);
                if (idx >= 0)
                {
                    var tail = pathSegments.Skip(idx + 1).ToArray();

                    // Wrapper detection: ".../<name>/<name>/..."
                    bool duplicateBefore = idx > 0 && pathSegments[idx - 1].Equals(name, StringComparison.OrdinalIgnoreCase);
                    if (duplicateBefore && immediateChildren.Count == 1)
                    {
                        var child = immediateChildren[0];
                        string adjusted = tail.Length > 0
                            ? $"{name}/{child}/{string.Join("/", tail)}"
                            : $"{name}/{child}";
                        AddCandidate(ref best, adjusted, folder, score: 1000 + idx + 100);
                    }
                    else
                    {
                        string adjusted = tail.Length > 0 ? $"{name}/{string.Join("/", tail)}" : name;
                        AddCandidate(ref best, adjusted, folder, score: 1000 + idx);
                    }
                }

                // ---- Phase 2: Flexible subdir match (inside project) ----
                var flex = BestSubdirFlexibleMatch(normalizedSubs, pathSegments, name);
                if (flex != null)
                {
                    var rest = string.Join("/", pathSegments.Skip(flex.MatchedLength));
                    string adjusted = string.IsNullOrEmpty(rest) ? flex.Subdir : $"{flex.Subdir}/{rest}";
                    AddCandidate(ref best, adjusted, folder,
                        score: 600 + flex.MatchedLength * 10 + flex.SubdirDepth);
                }
            }

            if (best != null)
                return (best.Value.adjusted, best.Value.folder);

            error = "The file path could not be reconciled with any granted folder.";
            return (null, null);
        }

         private static void AddCandidate(
            ref (string adjusted, DirectoryPicker folder, int score)? best,
            string adjusted,
            DirectoryPicker folder,
            int score)
        {
            if (best == null || score > best.Value.score)
                best = (adjusted, folder, score);
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
            public string Subdir { get; init; }          // normalized "<name>/..."
            public int MatchedLength { get; init; }      // # of initial path segments matched
            public int SubdirDepth { get; init; }        // number of segments in Subdir
        }

        /// <summary>
        /// Try to align a suffix of each normalized subdir's tail (after "<name>") to the beginning
        /// of the SARIF path. Pick the subdir that yields the longest match; prefer deeper subdirs on ties.
        /// </summary>
        private static FlexMatch BestSubdirFlexibleMatch(string[] normalizedSubdirs, string[] pathSegments, string rootName)
        {
            FlexMatch best = null;

            foreach (var sub in normalizedSubdirs)
            {
                var segs = sub.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length == 0 || !segs[0].Equals(rootName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var subTail = segs.Skip(1).ToArray(); // drop "<name>"

                // Try aligning path prefix to any suffix of subTail
                // Example: subTail = ["src","services","payment"]
                // path    = ["services","payment","stripe.js"]
                // align at offset=1 → matchLen=2
                for (int offset = 0; offset < subTail.Length; offset++)
                {
                    int matchLen = 0;
                    int max = Math.Min(pathSegments.Length, subTail.Length - offset);
                    while (matchLen < max &&
                           pathSegments[matchLen].Equals(subTail[offset + matchLen], StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen++;
                    }

                    if (matchLen > 0)
                    {
                        // On tie: prefer longer match; then prefer deeper subdir
                        if (best == null ||
                            matchLen > best.MatchedLength ||
                            (matchLen == best.MatchedLength && segs.Length > best.SubdirDepth))
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
            }

            return best;
        }
    }
}