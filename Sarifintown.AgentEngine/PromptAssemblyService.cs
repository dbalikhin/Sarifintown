using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace Sarifintown.AgentEngine;

public sealed class PromptAssemblyService : IPromptAssemblyService
{
    private const string PromptsRootRelativePath = ".sarif/sarifintown-prompts";
    private const string BundledPromptsDirectoryName = "sarifintown-prompts";
    private const string BaseDirectoryName = "base";
    private const string CategoriesDirectoryName = "categories";
    private const string OverridesDirectoryName = "org-overrides";

    private const string CoreDirectiveFileName = "core-directive.md";
    private const string OutputFormatFileName = "output-format.md";
    private const string SastCategoryFileName = "sast.md";
    private const string SastLegacyCategoryFileName = "sast"; // Not strictly needed, we can just use "sast.md"
    private const string SecretCategoryFileName = "secret.md";
    private const string ScaCategoryFileName = "sca.md";

    private readonly string _promptRootDirectory;
    private readonly PromptTemplateStyle _templateStyle;
    private readonly bool _enableSastModule;
    private readonly bool _enableSecretModule;
    private readonly bool _enableScaModule;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _fileCache = new(StringComparer.OrdinalIgnoreCase);

    public PromptAssemblyService(string? rootDirectoryPath = null)
    {
        _promptRootDirectory = ResolveRootDirectory(rootDirectoryPath);
        _templateStyle = PromptTemplateStyle.Structured;
        _enableSastModule = true;
        _enableSecretModule = true;
        _enableScaModule = true;
    }

    public PromptAssemblyService(IOptions<PromptAssemblyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _promptRootDirectory = ResolveRootDirectory(options.Value.RootDirectoryPath);
        _templateStyle = options.Value.TemplateStyle;
        _enableSastModule = options.Value.EnableSastModule;
        _enableSecretModule = options.Value.EnableSecretModule;
        _enableScaModule = options.Value.EnableScaModule;
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
        var outputFormatPath = Path.Combine(_promptRootDirectory, BaseDirectoryName, OutputFormatFileName);

        var sections = new List<string>
        {
            await ReadModuleOrMissingCommentAsync(coreDirectivePath, cancellationToken).ConfigureAwait(false)
        };

        if (_enableSastModule)
        {
            sections.Add(await ReadModuleOrMissingCommentAsync(Path.Combine(_promptRootDirectory, CategoriesDirectoryName, SastCategoryFileName), cancellationToken).ConfigureAwait(false));
        }

        if (_enableSecretModule)
        {
            sections.Add(await ReadModuleOrMissingCommentAsync(Path.Combine(_promptRootDirectory, CategoriesDirectoryName, SecretCategoryFileName), cancellationToken).ConfigureAwait(false));
        }

        if (_enableScaModule)
        {
            sections.Add(await ReadModuleOrMissingCommentAsync(Path.Combine(_promptRootDirectory, CategoriesDirectoryName, ScaCategoryFileName), cancellationToken).ConfigureAwait(false));
        }

        sections.Add(BuildFindingContextSection(resolvedRuleId, resolvedMessage, _templateStyle));

        var overrideSection = await BuildOverrideSectionAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(overrideSection))
        {
            sections.Add(overrideSection);
        }

        sections.Add(await ReadModuleOrMissingCommentAsync(outputFormatPath, cancellationToken).ConfigureAwait(false));

        return string.Join(Environment.NewLine, sections.Where(section => !string.IsNullOrWhiteSpace(section)));
    }

    /// <summary>
    /// Builds an aggregated LLM system prompt for multiple findings in a single string, avoiding duplication of global directives.
    /// </summary>
    public async Task<string> BuildBatchTriagePromptAsync(IEnumerable<(string RuleId, string Message)> findings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sections = new List<string>();

        var coreDirectivePath = Path.Combine(_promptRootDirectory, BaseDirectoryName, CoreDirectiveFileName);
        sections.Add(await ReadModuleOrMissingCommentAsync(coreDirectivePath, cancellationToken).ConfigureAwait(false));

        if (_enableSastModule)
        {
            sections.Add(await ReadModuleOrMissingCommentAsync(Path.Combine(_promptRootDirectory, CategoriesDirectoryName, SastCategoryFileName), cancellationToken).ConfigureAwait(false));
        }

        if (_enableSecretModule)
        {
            sections.Add(await ReadModuleOrMissingCommentAsync(Path.Combine(_promptRootDirectory, CategoriesDirectoryName, SecretCategoryFileName), cancellationToken).ConfigureAwait(false));
        }

        if (_enableScaModule)
        {
            sections.Add(await ReadModuleOrMissingCommentAsync(Path.Combine(_promptRootDirectory, CategoriesDirectoryName, ScaCategoryFileName), cancellationToken).ConfigureAwait(false));
        }

        foreach (var finding in findings.DistinctBy(f => $"{f.RuleId?.Trim() ?? string.Empty}:{f.Message?.Trim() ?? string.Empty}"))
        {
            sections.Add(BuildFindingContextSection(finding.RuleId, finding.Message, _templateStyle));
        }

        var overrideSection = await BuildOverrideSectionAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(overrideSection))
        {
            sections.Add(overrideSection);
        }

        var outputFormatPath = Path.Combine(_promptRootDirectory, BaseDirectoryName, OutputFormatFileName);
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

    private static string BuildFindingContextSection(string ruleId, string message, PromptTemplateStyle templateStyle)
    {
        var resolvedRule = string.IsNullOrWhiteSpace(ruleId) ? "unknown" : ruleId;
        var resolvedMessage = string.IsNullOrWhiteSpace(message) ? "- n/a" : message;

        var template = templateStyle switch
        {
            PromptTemplateStyle.Compact => CompactFindingContextTemplate,
            PromptTemplateStyle.Verbose => VerboseFindingContextTemplate,
            _ => StructuredFindingContextTemplate
        };

        return template
            .Replace("{{RULE_ID}}", resolvedRule, StringComparison.Ordinal)
            .Replace("{{MESSAGE}}", resolvedMessage, StringComparison.Ordinal)
            .TrimEnd();
    }

    private const string StructuredFindingContextTemplate = """
### Finding Context
- rule-id: `{{RULE_ID}}`
- source: `sarif-finding`

#### Finding Message
{{MESSAGE}}

#### Vulnerability Report Template
Keep `[Metadata]` and `[Description]` consistent across all extraction strategies.

```markdown
# Vulnerability Report

### [Metadata]
* **Rule ID:** `<rule-id>`
* **Category:** `<security-category>`
* **Primary File:** `<relative-file-path>`
* **Sink Node:** `<sink-api-or-call>`

### [Description]
<clear summary of why untrusted input reaches a dangerous sink>

### [Data Flow Evidence]
**[Step 1: Source]** `<file-path:line>`
```csharp
<source snippet>
```
**[Step 2: Propagator]** `<file-path:line>`
```csharp
<propagation snippet(s)>
```
**[Step N: Sink]** `<file-path:line>`
```csharp
<sink snippet>
```
```
""";

    private const string CompactFindingContextTemplate = """
### Finding Context
- rule-id: `{{RULE_ID}}`
- source: `sarif-finding`

#### Finding Message
{{MESSAGE}}

#### Vulnerability Report Template (Compact)
Use sections in this order: `[Metadata]`, `[Description]`, `[Data Flow Evidence]`.
""";

    private const string VerboseFindingContextTemplate = """
### Finding Context
- rule-id: `{{RULE_ID}}`
- source: `sarif-finding`

#### Finding Message
{{MESSAGE}}

#### Vulnerability Report Template (Verbose)
Always render `# Vulnerability Report`.
Always keep `### [Metadata]` and `### [Description]` unchanged across extraction modes.
Only alter `### [Data Flow Evidence]` according to the selected extraction strategy.
""";

    private async Task<string> ReadModuleOrMissingCommentAsync(string modulePath, CancellationToken cancellationToken)
    {
        if (_fileCache.TryGetValue(modulePath, out var cachedContent))
        {
            return cachedContent;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(modulePath))
        {
            var missing = CreateMissingFileComment(modulePath);
            _fileCache[modulePath] = missing;
            return missing;
        }

        try
        {
            var content = await File.ReadAllTextAsync(modulePath, cancellationToken).ConfigureAwait(false);
            _fileCache[modulePath] = content;
            return content;
        }
        catch (IOException)
        {
            var missing = CreateMissingFileComment(modulePath);
            _fileCache[modulePath] = missing;
            return missing;
        }
        catch (UnauthorizedAccessException)
        {
            var missing = CreateMissingFileComment(modulePath);
            _fileCache[modulePath] = missing;
            return missing;
        }
    }

    private static string ResolveRootDirectory(string? configuredRootDirectoryPath)
    {
        var workspaceRoot = WorkspaceSarifDiscovery.Discover().WorkspaceRoot;
        var bundledPromptsDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, BundledPromptsDirectoryName));

        if (string.IsNullOrWhiteSpace(configuredRootDirectoryPath))
        {
            var workspacePromptDirectory = Path.GetFullPath(Path.Combine(workspaceRoot, PromptsRootRelativePath));
            if (Directory.Exists(workspacePromptDirectory))
            {
                return workspacePromptDirectory;
            }

            return Directory.Exists(bundledPromptsDirectory)
                ? bundledPromptsDirectory
                : workspacePromptDirectory;
        }

        var configuredPromptDirectory = Path.IsPathRooted(configuredRootDirectoryPath)
            ? Path.GetFullPath(configuredRootDirectoryPath)
            : Path.GetFullPath(Path.Combine(workspaceRoot, configuredRootDirectoryPath));

        if (Directory.Exists(configuredPromptDirectory))
        {
            return configuredPromptDirectory;
        }

        return Directory.Exists(bundledPromptsDirectory)
            ? bundledPromptsDirectory
            : configuredPromptDirectory;
    }

    private string CreateMissingFileComment(string path)
    {
        var relativePath = Path.GetRelativePath(_promptRootDirectory, path).Replace('\\', '/');
        return $"<!-- missing-prompt-module: {relativePath} -->";
    }
}
