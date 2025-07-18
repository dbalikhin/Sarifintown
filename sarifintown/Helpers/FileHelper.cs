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

            // Remove URI prefix like "file://"
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("file://".Length).TrimStart('/');
            }

            // Standardize to forward slashes
            path = path.Replace('\\', '/');
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();

            foreach (var part in parts)
            {
                if (part == "..") // Handle "parent directory"
                {
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }
                }
                else if (part != ".") // Ignore "current directory"
                {
                    stack.Push(part);
                }
            }

            // Rebuild the canonical path
            return string.Join("/", stack.Reverse());
        }

        public static (string adjustedPath, DirectoryPicker matchedFolder) AdjustPathToGrantedFolder(
            string normalizedSarifPath,
            IEnumerable<DirectoryPicker> accessibleFolders,
            out string error)
        {
            error = null;
            var pathSegments = normalizedSarifPath.Split('/');

            foreach (var folder in accessibleFolders)
            {
                var folderSegments = new string[] { folder.Name };

                // SCENARIO 1: SARIF path is "above" the granted folder (e.g., /a/b/c, folder:c)
                int overlapIndex = FindSequenceIndex(pathSegments, folderSegments);
                if (overlapIndex != -1)
                {
                    string result = string.Join("/", pathSegments.Skip(overlapIndex + folderSegments.Length));
                    return (result, folder);
                }

                // SCENARIO 2: SARIF path is "below" the granted folder (e.g., d/e, folder: a/b/d)
                if (folder.Subdirectories != null)
                {
                    foreach (var sub in folder.Subdirectories)
                    {
                        var subSegments = sub.Split('/');
                        if (subSegments.Length > 0 && pathSegments.Length > 0 &&
                            string.Equals(subSegments.Last(), pathSegments.First(), StringComparison.OrdinalIgnoreCase))
                        {
                            // Prepend the known subdirectory path to the rest of the SARIF path
                            string result = string.Join("/", subSegments.Skip(1)) + "/" + string.Join("/", pathSegments.Skip(1));
                            return (result, folder);
                        }
                    }
                }
            }

            error = "The file path could not be reconciled with any granted folder.";
            return (null, null);

        }

        

    private static int FindSequenceIndex(string[] source, string[] target)
    {
        if (target.Length == 0 || source.Length < target.Length)
            return -1;

        for (int i = 0; i <= source.Length - target.Length; i++)
        {
            bool match = true;

            for (int j = 0; j < target.Length; j++)
            {
                if (!string.Equals(source[i + j], target[j], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }
    }
}
