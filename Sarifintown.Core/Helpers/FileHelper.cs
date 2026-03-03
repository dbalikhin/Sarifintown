using Sarifintown.Models;

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

        /// <summary>
        /// Resolves a SARIF artifact location to a normalized path by using URI, artifact index and originalUriBaseIds.
        /// </summary>
        public static string ResolveArtifactPath(PhysicalLocation.PhysicalLocationArtifactLocation artifactLocation, Run run)
        {
            ArgumentNullException.ThrowIfNull(run);

            if (artifactLocation == null)
            {
                return string.Empty;
            }

            var uri = artifactLocation.Uri;
            var uriBaseId = artifactLocation.UriBaseId;

            if ((string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(uriBaseId))
                && artifactLocation.Index.HasValue
                && run.Artifacts != null
                && artifactLocation.Index.Value >= 0
                && artifactLocation.Index.Value < run.Artifacts.Count)
            {
                var indexedLocation = run.Artifacts[artifactLocation.Index.Value]?.Location;
                if (indexedLocation != null)
                {
                    uri = string.IsNullOrWhiteSpace(uri) ? indexedLocation.Uri : uri;
                    uriBaseId = string.IsNullOrWhiteSpace(uriBaseId) ? indexedLocation.UriBaseId : uriBaseId;
                }
            }

            if (string.IsNullOrWhiteSpace(uri))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(uri, UriKind.Absolute, out var absoluteUri))
            {
                if (absoluteUri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizePath(absoluteUri.LocalPath);
                }

                return NormalizePath(absoluteUri.LocalPath);
            }

            if (!string.IsNullOrWhiteSpace(uriBaseId)
                && run.OriginalUriBaseIds != null
                && run.OriginalUriBaseIds.TryGetValue(uriBaseId, out var baseInfo))
            {
                var basePath = ResolveBasePathFromOriginalUriBaseIds(baseInfo, run.OriginalUriBaseIds);
                if (!string.IsNullOrWhiteSpace(basePath))
                {
                    if (Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri) &&
                        Uri.TryCreate(baseUri, uri, out var combinedUri))
                    {
                        if (combinedUri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
                        {
                            return NormalizePath(combinedUri.LocalPath);
                        }

                        return NormalizePath(combinedUri.LocalPath);
                    }

                    var combinedPath = Path.Combine(basePath.Replace('/', Path.DirectorySeparatorChar), uri.Replace('/', Path.DirectorySeparatorChar));
                    return NormalizePath(combinedPath);
                }
            }

            return NormalizePath(uri);
        }

        /// <summary>
        /// Rebases a normalized path to a workspace-relative path by folder segment name.
        /// </summary>
        public static string RebaseToWorkspaceRelativePath(string normalizedPath, string workspaceFolderName)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(workspaceFolderName))
            {
                return normalizedPath;
            }

            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var workspaceIndex = LastIndexOfSegment(segments, workspaceFolderName);
            if (workspaceIndex < 0 || workspaceIndex >= segments.Length - 1)
            {
                return normalizedPath;
            }

            return string.Join('/', segments.Skip(workspaceIndex + 1));
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

                var inferredScannerRootCandidates = immediateChildren
                    .Where(child => !string.IsNullOrWhiteSpace(child))
                    .Where(child => !child.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

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

                // Case: SARIF paths relative to scanner root (for example "Pages/Error.cshtml.cs")
                if (inferredScannerRootCandidates.Length == 1
                    && pathSegments.Length > 0
                    && !pathSegments[0].Equals(name, StringComparison.OrdinalIgnoreCase)
                    && !pathSegments[0].Equals(inferredScannerRootCandidates[0], StringComparison.OrdinalIgnoreCase))
                {
                    var inferredAdjusted = $"{inferredScannerRootCandidates[0]}/{string.Join("/", pathSegments)}";
                    AddCandidate(ref best, inferredAdjusted, folder, MatchTier.Flexible, bonus: 1);
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

        private static string ResolveBasePathFromOriginalUriBaseIds(UriBaseId baseInfo, IDictionary<string, UriBaseId> allBaseIds)
        {
            if (baseInfo == null || string.IsNullOrWhiteSpace(baseInfo.Uri))
            {
                return string.Empty;
            }

            var baseUri = baseInfo.Uri;
            if (string.IsNullOrWhiteSpace(baseInfo.ParentUriBaseId))
            {
                return baseUri;
            }

            if (!allBaseIds.TryGetValue(baseInfo.ParentUriBaseId, out var parentInfo))
            {
                return baseUri;
            }

            var parentBasePath = ResolveBasePathFromOriginalUriBaseIds(parentInfo, allBaseIds);
            if (string.IsNullOrWhiteSpace(parentBasePath))
            {
                return baseUri;
            }

            if (Uri.TryCreate(parentBasePath, UriKind.Absolute, out var parentUri)
                && Uri.TryCreate(parentUri, baseUri, out var combinedUri))
            {
                return combinedUri.ToString();
            }

            var combinedPath = Path.Combine(parentBasePath.Replace('/', Path.DirectorySeparatorChar), baseUri.Replace('/', Path.DirectorySeparatorChar));
            return NormalizePath(combinedPath);
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
