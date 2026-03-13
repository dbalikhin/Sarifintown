using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Sarifintown.AgentEngine;
using Sarifintown.Core;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Sarifintown.AgentEngine.Tests
{
    [TestFixture]
    public class V8TreeSitterEngineTests
    {
        private const string ManualDebugConfigFileName = "tree-sitter.debug.json";
        private static readonly JsonSerializerOptions DebugConfigJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private V8TreeSitterEngine _engine;

        [SetUp]
        public async Task Setup()
        {
            _engine = new V8TreeSitterEngine();
            await _engine.InitializeAsync();
        }

        [TearDown]
        public void TearDown()
        {
            _engine?.Dispose();
        }

        [Test]
        public async Task ExtractMethodAsync_WithValidCSharpCode_ReturnsMethodBody()
        {
            // Arrange
            var sourceCode = @"
            public class TestClass
            {
                public void TestMethod()
                {
                    int x = 1;
                }
            }";
            var language = "csharp";

            // Act
            var result = await _engine.ExtractMethodAsync(sourceCode, language, 2, 4);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Contains.Substring("public void TestMethod()"));
            Assert.That(result, Contains.Substring("int x = 1;"));
        }

        [Test]
        public async Task ExtractMethodAsync_WithValidJavascriptCode_ReturnsFunctionBody()
        {
            // Arrange
            var sourceCode = @"
            function testMethod() {
                let x = 1;
            }";
            var language = "javascript";

            // Act
            var result = await _engine.ExtractMethodAsync(sourceCode, language, 1, 1);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Contains.Substring("function testMethod()"));
        }

        [Test]
        public async Task ExtractMethodAsync_WithUnknownLanguage_ReturnsEmptyForFallback()
        {
            var sourceCode = "line1\nline2";

            var result = await _engine.ExtractMethodAsync(sourceCode, "unknownlang", 0, 0);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        [Explicit("Manual debug probe. Run this test under debugger to break immediately after Tree-sitter initialization.")]
        [NonParallelizable]
        public async Task InitializeAsync_ManualDebugProbe_HitsBreakpointAfterInitialization()
        {
            var config = LoadManualDebugConfig();

            using var engine = new V8TreeSitterEngine();
            await engine.InitializeAsync();

            var result = await engine.ExtractMethodAsync(
                config.SourceCode,
                config.Language,
                config.StartLine,
                config.EndLine);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        [Explicit("Manual workspace probe. Uses configured project_root/cwd and runs sarif_get includeEvidence=true with the V8 Tree-sitter engine.")]
        [NonParallelizable]
        public async Task SarifGet_WorkspaceProbe_UsesConfiguredRootAndInitializesV8Engine()
        {
            var config = LoadManualDebugConfig();
            if (string.IsNullOrWhiteSpace(config.ProjectRoot))
            {
                Assert.Ignore("`projectRoot` is not configured in tree-sitter.debug.json.");
            }

            var projectRoot = Path.GetFullPath(config.ProjectRoot);
            if (!Directory.Exists(projectRoot))
            {
                Assert.Ignore($"Configured project root does not exist: {projectRoot}");
            }

            var originalCurrentDirectory = Directory.GetCurrentDirectory();

            try
            {
                if (config.SetCurrentDirectory)
                {
                    Directory.SetCurrentDirectory(projectRoot);
                }

                var discovery = WorkspaceSarifDiscovery.Discover(projectRoot);
                if (discovery.SarifFiles.Count == 0)
                {
                    Assert.Ignore($"No SARIF files were found under: {discovery.SarifDirectory}");
                }

                using var v8Engine = new V8TreeSitterEngine();
                await v8Engine.InitializeAsync();

                SarifTools.FileReader = new NativeFileReader(discovery.WorkspaceRoot);
                SarifTools.TreeSitterEngine = v8Engine;
                SetInternalSarifToolsProperty("StateService", null);
                SetInternalSarifToolsProperty("SnippetCache", null);
                SetInternalSarifToolsProperty("SnippetWarmupService", null);
                SarifTools.SetWorkspaceRoot(discovery.WorkspaceRoot);
                SarifTools.SetDiscoveredSarifFiles(discovery.SarifFiles);
                SarifTools.SetLocalUiBaseUrl(string.Empty);

                if (!string.IsNullOrWhiteSpace(config.Filter))
                {
                    await SarifTools.SarifFilter(config.Filter);
                }

                var result = await SarifTools.SarifGet(
                    limit: config.Limit);

                var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
                Assert.That(text, Contains.Substring("## SARIF Scoped Query"));
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCurrentDirectory);
            }
        }

        private static ManualDebugConfig LoadManualDebugConfig()
        {
            var configPath = Path.Combine(TestContext.CurrentContext.TestDirectory, ManualDebugConfigFileName);
            var config = ManualDebugConfig.Default;

            if (File.Exists(configPath))
            {
                var raw = File.ReadAllText(configPath);
                var parsed = JsonSerializer.Deserialize<ManualDebugConfig>(raw, DebugConfigJsonOptions);
                if (parsed != null)
                {
                    config = parsed;
                }
            }

            if (string.IsNullOrWhiteSpace(config.ProjectRoot))
            {
                var workspaceFromEnv = Environment.GetEnvironmentVariable("PROJECT_ROOT");
                if (string.IsNullOrWhiteSpace(workspaceFromEnv))
                {
                    workspaceFromEnv = Environment.GetEnvironmentVariable("WORKSPACE_ROOT");
                }

                if (!string.IsNullOrWhiteSpace(workspaceFromEnv))
                {
                    config = config with { ProjectRoot = workspaceFromEnv };
                }
            }

            return config;
        }

        private static void SetInternalSarifToolsProperty(string propertyName, object? value)
        {
            var property = typeof(SarifTools).GetProperty(propertyName, BindingFlags.Static | BindingFlags.NonPublic);
            property?.SetValue(null, value);
        }

        private sealed record ManualDebugConfig(
            string Language,
            string SourceCode,
            int StartLine,
            int EndLine,
            string ProjectRoot,
            bool SetCurrentDirectory,
            bool IncludeEvidence,
            int Limit,
            string Scope,
            string Filter)
        {
            public static ManualDebugConfig Default { get; } = new(
                "csharp",
                """
                public class DebugSample
                {
                    public void Run()
                    {
                        var value = 42;
                    }
                }
                """,
                2,
                5,
                "C:/dmitry/SharpSaster",
                true,
                true,
                25,
                "keep",
                string.Empty);
        }
    }
}