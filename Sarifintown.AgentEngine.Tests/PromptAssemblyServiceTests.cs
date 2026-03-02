using NUnit.Framework;
using Microsoft.Extensions.Options;

namespace Sarifintown.AgentEngine.Tests;

[TestFixture]
public class PromptAssemblyServiceTests
{
    [Test]
    public async Task BuildTriagePromptAsync_WhenSqlSignalPresent_UsesSqlCategoryModule()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "sast-sqli.md", "# sql module");
            WriteCategoryFile(promptRoot, "sast-xss.md", "# xss module");
            WriteCategoryFile(promptRoot, "secret-exposure.md", "# secret module");
            WriteCategoryFile(promptRoot, "default-sast.md", "# default module");

            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync("CA3001", "Possible SQL injection");

            Assert.That(prompt.Contains("# sql module", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [Test]
    public async Task BuildTriagePromptAsync_WhenTemplateStyleCompact_UsesCompactTemplate()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "default-sast.md", "# default module");

            var options = Options.Create(new PromptAssemblyOptions
            {
                RootDirectoryPath = promptRoot,
                TemplateStyle = PromptTemplateStyle.Compact
            });

            var service = new PromptAssemblyService(options);
            var prompt = await service.BuildTriagePromptAsync("RULE-2", "message");

            Assert.That(prompt.Contains("Vulnerability Report Template (Compact)", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [Test]
    public async Task BuildTriagePromptAsync_WhenTemplateStyleVerbose_UsesVerboseTemplate()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "default-sast.md", "# default module");

            var options = Options.Create(new PromptAssemblyOptions
            {
                RootDirectoryPath = promptRoot,
                TemplateStyle = PromptTemplateStyle.Verbose
            });

            var service = new PromptAssemblyService(options);
            var prompt = await service.BuildTriagePromptAsync("RULE-3", "message");

            Assert.That(prompt.Contains("Vulnerability Report Template (Verbose)", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [Test]
    public async Task BuildTriagePromptAsync_IncludesOption2EvidenceRenderingGuidance()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "default-sast.md", "# default module");

            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync("RULE-1", "message");

            Assert.That(prompt.Contains("### [Metadata]", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("### [Description]", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("### [Data Flow Evidence]", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("Option 2.2 (`line ±3 concatenated blocks`)", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("if adjacent steps differ by <= 6 lines", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("Option 2.3 (`tree-sitter method extraction`)", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [Test]
    public async Task BuildTriagePromptAsync_WhenRequiredFilesMissing_IncludesMarkdownMissingComments()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync("UnknownRule", "Unknown message");

            Assert.That(prompt.Contains("<!-- missing-prompt-module: base/core-directive.md -->", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [Test]
    public async Task BuildTriagePromptAsync_WhenOverridesExist_IncludesOverridesHeader()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "default-sast.md", "# default module");

            var overridesPath = Path.Combine(promptRoot, "org-overrides");
            Directory.CreateDirectory(overridesPath);
            File.WriteAllText(Path.Combine(overridesPath, "aaa.md"), "override-a");
            File.WriteAllText(Path.Combine(overridesPath, "bbb.md"), "override-b");

            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync("Rule", "message");

            Assert.That(prompt.Contains("### Organizational Policies & Accepted Risks", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [TestCase("rule-xss", "", "# xss module")]
    [TestCase("", "cross site scripting", "# xss module")]
    [TestCase("", "secret leaked key", "# secret module")]
    [TestCase("", "generic rule", "# default module")]
    public async Task BuildTriagePromptAsync_RoutesToExpectedCategory(string ruleId, string message, string expectedCategoryMarker)
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "sast-sqli.md", "# sql module");
            WriteCategoryFile(promptRoot, "sast-xss.md", "# xss module");
            WriteCategoryFile(promptRoot, "secret-exposure.md", "# secret module");
            WriteCategoryFile(promptRoot, "default-sast.md", "# default module");

            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync(ruleId, message);

            Assert.That(prompt.Contains(expectedCategoryMarker, StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    private static string CreatePromptRoot()
    {
        var promptRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(promptRoot);
        return promptRoot;
    }

    private static void WriteCoreFiles(string promptRoot)
    {
        var basePath = Path.Combine(promptRoot, "base");
        Directory.CreateDirectory(basePath);
        File.WriteAllText(Path.Combine(basePath, "core-directive.md"), "# core");
        File.WriteAllText(Path.Combine(basePath, "output-format.md"), "# output");
    }

    private static void WriteCategoryFile(string promptRoot, string fileName, string content)
    {
        var categoriesPath = Path.Combine(promptRoot, "categories");
        Directory.CreateDirectory(categoriesPath);
        File.WriteAllText(Path.Combine(categoriesPath, fileName), content);
    }
}
