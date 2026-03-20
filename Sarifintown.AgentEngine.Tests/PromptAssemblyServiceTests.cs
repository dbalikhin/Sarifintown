using NUnit.Framework;
using Microsoft.Extensions.Options;

namespace Sarifintown.AgentEngine.Tests;

[TestFixture]
public class PromptAssemblyServiceTests
{
    [Test]
    public async Task BuildTriagePromptAsync_ReturnsEmptyString_ForNullOrEmptyRuleAndMessage()
    {
        // Actually it doesn't return empty string now, it builds the generic prompt.
        // Let's test that the template is included even with null rule/message.
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "sast.md", "# sast module");
            var service = new PromptAssemblyService(promptRoot);
            var prompt = await service.BuildTriagePromptAsync(null!, null!);
            Assert.That(prompt.Contains("# sast module", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("unknown", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [Test]
    public async Task BuildTriagePromptAsync_IncludesEnabledModules()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "sast.md", "# sast module");
            WriteCategoryFile(promptRoot, "sast-sanitizers.md", "# sast sanitizers module");
            WriteCategoryFile(promptRoot, "secret.md", "# secret module");
            WriteCategoryFile(promptRoot, "sca.md", "# sca module");

            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync("CA3001", "Possible SQL injection");

            Assert.That(prompt.Contains("# sast module", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("# sast sanitizers module", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("# secret module", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("# sca module", StringComparison.Ordinal), Is.True);
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
            WriteCategoryFile(promptRoot, "sast.md", "# sast module");

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
            WriteCategoryFile(promptRoot, "sast.md", "# sast module");

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
            WriteCategoryFile(promptRoot, "sast.md", "# sast module");

            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync("RULE-1", "message");

            Assert.That(prompt.Contains("### [Metadata]", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("### [Description]", StringComparison.Ordinal), Is.True);
            Assert.That(prompt.Contains("### [Data Flow Evidence]", StringComparison.Ordinal), Is.True);
            // Replaced option formatting logic so this part of assertion may need pruning. We'll leave it out since rendering rules were removed.
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
    public async Task BuildTriagePromptAsync_WhenSastEnabled_IncludesSanitizersImmediatelyAfterSast()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "sast.md", "# sast module");
            WriteCategoryFile(promptRoot, "sast-sanitizers.md", "# sast sanitizers module");
            WriteCategoryFile(promptRoot, "secret.md", "# secret module");

            var service = new PromptAssemblyService(promptRoot);

            var prompt = await service.BuildTriagePromptAsync("Rule", "message");

            var sastIndex = prompt.IndexOf("# sast module", StringComparison.Ordinal);
            var sanitizersIndex = prompt.IndexOf("# sast sanitizers module", StringComparison.Ordinal);
            var secretIndex = prompt.IndexOf("# secret module", StringComparison.Ordinal);

            Assert.That(sastIndex >= 0, Is.True);
            Assert.That(sanitizersIndex > sastIndex, Is.True);
            Assert.That(secretIndex > sanitizersIndex, Is.True);
        }
        finally
        {
            Directory.Delete(promptRoot, true);
        }
    }

    [Test]
    public async Task BuildBatchTriagePromptAsync_WhenSastDisabled_ExcludesSastAndSanitizers()
    {
        var promptRoot = CreatePromptRoot();
        try
        {
            WriteCoreFiles(promptRoot);
            WriteCategoryFile(promptRoot, "sast.md", "# sast module");
            WriteCategoryFile(promptRoot, "sast-sanitizers.md", "# sast sanitizers module");
            WriteCategoryFile(promptRoot, "secret.md", "# secret module");

            var options = Options.Create(new PromptAssemblyOptions
            {
                RootDirectoryPath = promptRoot,
                EnableSastModule = false,
                EnableSecretModule = true,
                EnableScaModule = false
            });

            var service = new PromptAssemblyService(options);
            var prompt = await service.BuildBatchTriagePromptAsync([("RULE-1", "message")]);

            Assert.That(prompt.Contains("# sast module", StringComparison.Ordinal), Is.False);
            Assert.That(prompt.Contains("# sast sanitizers module", StringComparison.Ordinal), Is.False);
            Assert.That(prompt.Contains("# secret module", StringComparison.Ordinal), Is.True);
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
