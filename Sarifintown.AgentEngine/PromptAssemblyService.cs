using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace Sarifintown.AgentEngine;

public sealed class PromptAssemblyService : IPromptAssemblyService
{
    private const string PromptsRootRelativePath = ".sarif/sarifintown-prompts";
    private const string BaseDirectoryName = "base";
    private const string CategoriesDirectoryName = "categories";
    private const string OverridesDirectoryName = "org-overrides";

    private const string CoreDirectiveFileName = "core-directive.md";
    private const string OutputFormatFileName = "output-format.md";
    private const string SqlCategoryFileName = "sast-sqli.md";
    private const string XssCategoryFileName = "sast-xss.md";
    private const string SecretCategoryFileName = "secret-exposure.md";
    private const string DefaultCategoryFileName = "default-sast.md";

    private readonly string _promptRootDirectory;

    public PromptAssemblyService(string? rootDirectoryPath = null)
    {
        _promptRootDirectory = ResolveRootDirectory(rootDirectoryPath);
    }

    public PromptAssemblyService(IOptions<PromptAssemblyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _promptRootDirectory = ResolveRootDirectory(options.Value.RootDirectoryPath);
    }

    /// <summary>
    /// Builds a triage-ready LLM system prompt from modular markdown templates and finding context.
    /// </summary>
    public async Task<string> BuildTriagePromptAsync(string ruleId, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedRuleId = ruleId?.Trim() ?? string.Empty;
        var resolvedMessage = message?.Trim() ?? string.Empty;

        var coreDirectivePath = Path.Combine(_promptRootDirectory, BaseDirectoryName, CoreDirectiveFileName);
        var categoryModulePath = Path.Combine(
            _promptRootDirectory,
            CategoriesDirectoryName,
            DetermineCategoryModule(resolvedRuleId, resolvedMessage));
        var outputFormatPath = Path.Combine(_promptRootDirectory, BaseDirectoryName, OutputFormatFileName);

        var sections = new List<string>
        {
            await ReadModuleOrMissingCommentAsync(coreDirectivePath, cancellationToken).ConfigureAwait(false),
            await ReadModuleOrMissingCommentAsync(categoryModulePath, cancellationToken).ConfigureAwait(false),
            BuildFindingContextSection(resolvedRuleId, resolvedMessage)
        };

        var overrideSection = await BuildOverrideSectionAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(overrideSection))
        {
            sections.Add(overrideSection);
        }

        sections.Add(await ReadModuleOrMissingCommentAsync(outputFormatPath, cancellationToken).ConfigureAwait(false));

        return string.Join(Environment.NewLine, sections.Where(section => !string.IsNullOrWhiteSpace(section)));
    }

    private async Task<string> BuildOverrideSectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var overridesDirectoryPath = Path.Combine(_promptRootDirectory, OverridesDirectoryName);
        if (!Directory.Exists(overridesDirectoryPath))
        {
            return string.Empty;
        }

        var overrideFiles = Directory
            .EnumerateFiles(overridesDirectoryPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (overrideFiles.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("### Organizational Policies & Accepted Risks");

        for (var index = 0; index < overrideFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await ReadModuleOrMissingCommentAsync(overrideFiles[index], cancellationToken).ConfigureAwait(false);
            builder.AppendLine(content);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildFindingContextSection(string ruleId, string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("### Finding Context");
        builder.AppendLine($"- rule-id: `{(string.IsNullOrWhiteSpace(ruleId) ? "unknown" : ruleId)}`");
        builder.AppendLine("- source: `sarif-finding`");
        builder.AppendLine();
        builder.AppendLine("#### Finding Message");
        builder.AppendLine(string.IsNullOrWhiteSpace(message) ? "- n/a" : message);
        builder.AppendLine();
        builder.AppendLine("#### Evidence Template");
        builder.AppendLine("- file: `<relative-file-path>`");
        builder.AppendLine("- location: `<start-line[:start-column]-end-line[:end-column]>`");
        builder.AppendLine("- language: `<programming-language>`");
        builder.AppendLine("- data-flow: `source -> propagation -> sink` (markdown bullet list)");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine("<code-snippet-from-sarif-or-source>");
        builder.AppendLine("```");

        return builder.ToString().TrimEnd();
    }

    private static string DetermineCategoryModule(string ruleId, string message)
    {
        var normalized = NormalizeForMatching($"{ruleId} {message}");

        if (normalized.Contains("sqli", StringComparison.Ordinal) || normalized.Contains("sql", StringComparison.Ordinal))
        {
            return SqlCategoryFileName;
        }

        if (normalized.Contains("xss", StringComparison.Ordinal) || normalized.Contains("crosssitescripting", StringComparison.Ordinal))
        {
            return XssCategoryFileName;
        }

        if (normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("key", StringComparison.Ordinal))
        {
            return SecretCategoryFileName;
        }

        return DefaultCategoryFileName;
    }

    private async Task<string> ReadModuleOrMissingCommentAsync(string modulePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(modulePath))
        {
            return CreateMissingFileComment(modulePath);
        }

        try
        {
            return await File.ReadAllTextAsync(modulePath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return CreateMissingFileComment(modulePath);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateMissingFileComment(modulePath);
        }
    }

    private static string ResolveRootDirectory(string? configuredRootDirectoryPath)
    {
        var workspaceRoot = WorkspaceSarifDiscovery.Discover().WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(configuredRootDirectoryPath))
        {
            return Path.GetFullPath(Path.Combine(workspaceRoot, PromptsRootRelativePath));
        }

        return Path.IsPathRooted(configuredRootDirectoryPath)
            ? Path.GetFullPath(configuredRootDirectoryPath)
            : Path.GetFullPath(Path.Combine(workspaceRoot, configuredRootDirectoryPath));
    }

    private string CreateMissingFileComment(string path)
    {
        var relativePath = Path.GetRelativePath(_promptRootDirectory, path).Replace('\\', '/');
        return $"<!-- missing-prompt-module: {relativePath} -->";
    }

    private static string NormalizeForMatching(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var lowered = input.ToLowerInvariant();
        return Regex.Replace(lowered, "[^a-z0-9]", string.Empty, RegexOptions.CultureInvariant);
    }
}
