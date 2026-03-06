namespace Sarifintown.AgentEngine;

internal static class CodeLanguageResolver
{
    internal static string GetLanguageFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".py" => "python",
            ".java" => "java",
            ".cpp" => "cpp",
            ".c" => "c",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".php" => "php",
            ".html" => "html",
            ".css" => "css",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" => "yaml",
            ".yml" => "yaml",
            ".md" => "markdown",
            ".sh" => "bash",
            ".ps1" => "powershell",
            ".sql" => "sql",
            _ => string.Empty
        };
    }
}
