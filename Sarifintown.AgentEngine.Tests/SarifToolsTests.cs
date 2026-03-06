using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Sarifintown.AgentEngine;
using Sarifintown.Core;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Sarifintown.AgentEngine.Tests
{
    [TestFixture]
    public class SarifToolsTests
    {
        private class FakeFileReader : IFileReader
        {
            public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

            public Task<string> ReadFileAsync(string relativePath)
            {
                if (Files.TryGetValue(relativePath, out var content))
                {
                    return Task.FromResult(content);
                }

                if (File.Exists(relativePath))
                {
                    return File.ReadAllTextAsync(relativePath);
                }

                throw new FileNotFoundException($"File not found: {relativePath}");
            }
        }

        [Test]
        public async Task SarifTriage_WithDisplayAliasTarget_ResolvesToUnderlyingFindingId()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "alias-target.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-ALIAS",
                      "level": "error",
                      "message": { "text": "alias target" }
                    }
                  ]
                }
              ]
            }
            """);

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { sarifPath });

            try
            {
                var getResult = await SarifTools.SarifGet(scope: "set", filter: "severity:high", limit: 10);
                var stateContext = JsonSerializer.SerializeToElement(getResult.Meta);
                var alias = stateContext
                    .GetProperty("context")
                    .GetProperty("aliases")[0]
                    .GetProperty("displayid")
                    .GetString();

                Assert.That(alias, Is.Not.Null.And.Not.Empty);

                var triageResult = await SarifTools.SarifTriage(
                    state: "confirmed",
                    reason: "alias-validated",
                    target: alias!);

                var triageText = triageResult.Content[0] as TextContentBlock;
                Assert.That(triageText, Is.Not.Null);
                Assert.That(triageText!.Text, Contains.Substring("Affected findings: **1**"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        private class FakeTreeSitterEngine : ITreeSitterEngine
        {
            private static int _extractMethodCallCount;

            public static int ExtractMethodCallCount => Volatile.Read(ref _extractMethodCallCount);

            public static void Reset()
            {
                Interlocked.Exchange(ref _extractMethodCallCount, 0);
            }

            public Task InitializeAsync() => Task.CompletedTask;

            public Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine, CancellationToken cancellationToken = default)
            {
                return Task.FromResult($"Extracted: {language} from {startLine} to {endLine}");
            }
        }

        [SetUp]
        public void Setup()
        {
            SarifTools.FileReader = new FakeFileReader();
            SarifTools.TreeSitterEngine = new FakeTreeSitterEngine();
            FakeTreeSitterEngine.Reset();
            SetInternalSarifToolsProperty("StateService", null);
            SetInternalSarifToolsProperty("SnippetCache", null);
            SetInternalSarifToolsProperty("SnippetWarmupService", null);
            SarifTools.SetDiscoveredSarifFiles(Array.Empty<string>());
            SarifTools.SetLocalUiBaseUrl(string.Empty);
            SarifTools.SetWorkspaceRoot(Directory.GetCurrentDirectory());
        }

        [Test]
        public async Task SarifGet_WithScopeLifecycle_ReturnsScopedEnvelopeAndMetrics()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "scope-lifecycle.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-HIGH",
                      "level": "error",
                      "message": { "text": "high" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/high.cs" },
                            "region": { "startLine": 10 }
                          }
                        }
                      ]
                    },
                    {
                      "ruleId": "RULE-MED",
                      "level": "warning",
                      "message": { "text": "med" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/med.cs" },
                            "region": { "startLine": 20 }
                          }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { sarifPath });

            try
            {
                var setResult = await SarifTools.SarifGet(scope: "set", filter: "severity:high", limit: 10);
                var setMeta = JsonSerializer.SerializeToElement(setResult.Meta);

                Assert.That(setMeta.GetProperty("context").GetProperty("active_scope").GetProperty("severity").GetString(), Is.EqualTo("high"));
                Assert.That(setMeta.GetProperty("context").GetProperty("metrics").GetProperty("total_in_scope").GetInt32(), Is.EqualTo(1));
                Assert.That(setMeta.GetProperty("pause").GetBoolean(), Is.True);
                Assert.That(setMeta.GetProperty("next_step").GetString(), Is.EqualTo("sarif_triage"));
                Assert.That(((TextContentBlock)setResult.Content[0]).Text, Contains.Substring("## SARIF Scoped Query"));
                Assert.That(((TextContentBlock)setResult.Content[0]).Text, Contains.Substring(SarifTools.StateContextDelimiter));

                var refineResult = await SarifTools.SarifGet(scope: "refine", filter: "rule:RULE-HIGH", limit: 10);
                var refineMeta = JsonSerializer.SerializeToElement(refineResult.Meta);

                Assert.That(refineMeta.GetProperty("context").GetProperty("active_scope").GetProperty("severity").GetString(), Is.EqualTo("high"));
                Assert.That(refineMeta.GetProperty("context").GetProperty("active_scope").GetProperty("rule").GetString(), Is.EqualTo("RULE-HIGH"));

                var clearResult = await SarifTools.SarifGet(scope: "clear", limit: 10);
                var clearMeta = JsonSerializer.SerializeToElement(clearResult.Meta);

                Assert.That(clearMeta.GetProperty("context").GetProperty("active_scope").EnumerateObject().Count(), Is.EqualTo(0));
                Assert.That(clearMeta.GetProperty("context").GetProperty("metrics").GetProperty("total_in_scope").GetInt32(), Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifGet_WithLargeLimit_CapsReturnedBatchToHardLimit()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var resultsJson = string.Join(",", Enumerable.Range(1, 30).Select(index =>
                $$"""
                  {
                    "ruleId": "RULE-{{index}}",
                    "level": "error",
                    "message": { "text": "finding {{index}}" }
                  }
                """));

            var sarifPath = Path.Combine(sarifDirectory, "hard-limit.sarif");
            File.WriteAllText(sarifPath, $$"""
            {
              "runs": [
                {
                  "results": [
            {{resultsJson}}
                  ]
                }
              ]
            }
            """);

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { sarifPath });

            try
            {
                var result = await SarifTools.SarifGet(scope: "keep", includeEvidence: false, limit: 100);
                var meta = JsonSerializer.SerializeToElement(result.Meta);

                Assert.That(meta.GetProperty("context").GetProperty("metrics").GetProperty("returned_in_batch").GetInt32(), Is.EqualTo(25));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifTriage_WithScopeTarget_AppliesDecisionToActiveScope()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "scope-target.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-ONE",
                      "level": "error",
                      "message": { "text": "one" }
                    },
                    {
                      "ruleId": "RULE-TWO",
                      "level": "error",
                      "message": { "text": "two" }
                    },
                    {
                      "ruleId": "RULE-THREE",
                      "level": "warning",
                      "message": { "text": "three" }
                    }
                  ]
                }
              ]
            }
            """);

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { sarifPath });

            try
            {
                await SarifTools.SarifGet(scope: "set", filter: "severity:high");

                var triageResult = await SarifTools.SarifTriage(
                    state: "confirmed",
                    reason: "validated",
                    target: "scope");

                var triageText = triageResult.Content[0] as TextContentBlock;
                Assert.That(triageText, Is.Not.Null);
                Assert.That(triageText!.Text, Contains.Substring("Affected findings: **2**"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public void SarifTriage_WithInvalidState_ThrowsArgumentException()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await SarifTools.SarifTriage(state: "tp", reason: "invalid", target: "scope"));
        }

        [Test]
        public void ResolveInteractiveSurface_WithCursorHostHint_ReturnsUiUri()
        {
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "Cursor");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            Assert.That(payload.GetProperty("uri").GetString(), Is.EqualTo("ui://sarifintown/mcp/dashboard"));
            Assert.That(payload.GetProperty("bridge").GetProperty("transport").GetString(), Is.EqualTo("postMessage"));
            Assert.That(payload.GetProperty("bridge").GetProperty("channel").GetString(), Is.EqualTo("sarifintown.mcp.v1"));
        }

        [Test]
        public void GenerateAnalysisReport_WithValidData_CreatesMarkdownFile()
        {
            var resultId = "0";
            var extractedFlowData = @"{
                ""rule_id"": ""RULE001"",
                ""flow_steps"": [
                    {
                        ""file_path"": ""test.cs"",
                        ""start_line"": 10,
                        ""message"": ""Step 1"",
                        ""code_snippet"": ""var x = 1;""
                    }
                ]
            }";
            var outputPath = Path.GetTempFileName();

            try
            {
                var result = SarifTools.GenerateAnalysisReport(resultId, extractedFlowData, outputPath);

                Assert.That(result, Contains.Substring("Report generated successfully"));
                var fileContent = File.ReadAllText(outputPath);
                Assert.That(fileContent, Contains.Substring("# Vulnerability Analysis Report"));
                Assert.That(fileContent, Contains.Substring("**Rule ID:** RULE001"));
                Assert.That(fileContent, Contains.Substring("test.cs (Line 10)"));
                Assert.That(fileContent, Contains.Substring("var x = 1;"));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        [Test]
        public void McpToolExposure_ContainsStatefulTools()
        {
            var toolMethods = typeof(SarifTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Length > 0)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.That(toolMethods, Is.EqualTo(new[] { "SarifGet", "SarifTriage" }));
        }

        private static void SetInternalSarifToolsProperty(string propertyName, object? value)
        {
            var property = typeof(SarifTools).GetProperty(propertyName, BindingFlags.Static | BindingFlags.NonPublic);
            property!.SetValue(null, value);
        }
    }
}
