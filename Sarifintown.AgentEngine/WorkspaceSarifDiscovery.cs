namespace Sarifintown.AgentEngine;

public sealed class WorkspaceSarifDiscoveryResult
{
    public required string WorkspaceRoot { get; init; }
    public required string SarifDirectory { get; init; }
    public required IReadOnlyList<string> SarifFiles { get; init; }
}

public static class WorkspaceSarifDiscovery
{
    private static readonly string[] WorkspaceEnvironmentVariables =
    [
        "PROJECT_ROOT",
        "WORKSPACE_FOLDER",
        "WORKSPACE_ROOT",
        "MCP_WORKSPACE_ROOT",
        "PWD"
    ];

    private static readonly string[] UnresolvedWorkspaceTokens =
    [
        "{workspaceFolder}",
        "${workspaceFolder}",
        "${workspaceRoot}",
        "{workspaceRoot}",
        "$PROJECT_DIR$"
    ];

    private const string SarifDirectoryName = ".sarif";

    public static WorkspaceSarifDiscoveryResult Discover(string? workspaceRoot = null)
    {
        var resolvedWorkspaceRoot = ResolveWorkspaceRoot(workspaceRoot);
        var sarifDirectory = Path.Combine(resolvedWorkspaceRoot, SarifDirectoryName);

        if (!Directory.Exists(sarifDirectory))
        {
            return new WorkspaceSarifDiscoveryResult
            {
                WorkspaceRoot = resolvedWorkspaceRoot,
                SarifDirectory = sarifDirectory,
                SarifFiles = Array.Empty<string>()
            };
        }

        var sarifFiles = Directory
            .EnumerateFiles(sarifDirectory, "*.sarif", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorkspaceSarifDiscoveryResult
        {
            WorkspaceRoot = resolvedWorkspaceRoot,
            SarifDirectory = sarifDirectory,
            SarifFiles = sarifFiles
        };
    }

    private static string ResolveWorkspaceRoot(string? workspaceRoot)
    {
        if (TryGetValidPath(workspaceRoot, out var explicitPath))
        {
            return explicitPath;
        }

        foreach (var environmentVariable in WorkspaceEnvironmentVariables)
        {
            if (TryGetValidPath(Environment.GetEnvironmentVariable(environmentVariable), out var envPath))
            {
                return envPath;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool TryGetValidPath(string? candidatePath, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var trimmedPath = candidatePath.Trim();
        if (UnresolvedWorkspaceTokens.Contains(trimmedPath, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmedPath.StartsWith("${", StringComparison.Ordinal) && trimmedPath.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmedPath.StartsWith("{", StringComparison.Ordinal) && trimmedPath.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        fullPath = Path.GetFullPath(trimmedPath);
        return true;
    }
}
