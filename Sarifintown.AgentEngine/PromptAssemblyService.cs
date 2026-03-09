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
    private const string SqlCategoryFileName = "sast-sqli.md";
    private const string XssCategoryFileName = "sast-xss.md";
    private const string SecretCategoryFileName = "secret-exposure.md";
    private const string DefaultCategoryFileName = "default-sast.md";

    private readonly string _promptRootDirectory;
    private readonly PromptTemplateStyle _templateStyle;

    public PromptAssemblyService(string? rootDirectoryPath = null)
    {
        _promptRootDirectory = ResolveRootDirectory(rootDirectoryPath);
        _templateStyle = PromptTemplateStyle.Structured;
    }

    public PromptAssemblyService(IOptions<PromptAssemblyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _promptRootDirectory = ResolveRootDirectory(options.Value.RootDirectoryPath);
        _templateStyle = options.Value.TemplateStyle;
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
            BuildFindingContextSection(resolvedRuleId, resolvedMessage, _templateStyle)
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
Keep `[Metadata]` and `[Description]` consistent across all extraction strategies. Change only `[Data Flow Evidence]` rendering.

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

#### Data Flow Rendering Rules
- Option 2.1 (`line ±3 strict separation`): output one step header plus one code block per step.
- Option 2.2 (`line ±3 concatenated blocks`): group by `file_path`, sort by `line_number`, and if adjacent steps differ by <= 6 lines emit sequential step headers followed by one shared code block.
- Option 2.3 (`tree-sitter method extraction`): if steps resolve to the same method node, emit sequential step headers followed by one shared full-method code block.
- Use `Source`, `Propagator`, and `Sink` labels in each step header.
""";

    private const string CompactFindingContextTemplate = """
### Finding Context
- rule-id: `{{RULE_ID}}`
- source: `sarif-finding`

#### Finding Message
{{MESSAGE}}

#### Vulnerability Report Template (Compact)
Use sections in this order: `[Metadata]`, `[Description]`, `[Data Flow Evidence]`.

#### Data Flow Rendering Rules
- Option 2.1: one code block per step (`line ±3`).
- Option 2.2: same file + line distance <= 6 => one shared code block.
- Option 2.3: same tree-sitter method => one shared full-method code block.
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

#### Data Flow Rendering Rules
1. Group steps by `file_path`.
2. Sort steps by `line_number`.
3. Option 2.1 (`line ±3 strict separation`): emit one step header + one code block for each step.
4. Option 2.2 (`line ±3 concatenated blocks`): if `Step[N+1].Line_Number - Step[N].Line_Number <= 6`, emit sequential step headers then one code block.
5. Option 2.3 (`tree-sitter method extraction`): if steps are inside the same method AST node, emit sequential step headers then one shared method block.
6. Use labels `Source`, `Propagator`, `Sink` for each step header.
""";

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
