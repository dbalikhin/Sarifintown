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
        public async Task SarifGet_WithKeepAndNoPageToken_AutoAdvancesWithinSameScope()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var resultsJson = string.Join(",", Enumerable.Range(1, 3).Select(index =>
                $$"""
                  {
                    "ruleId": "RULE-{{index}}",
                    "level": "error",
                    "message": { "text": "finding {{index}}" }
                  }
                """));

            var sarifPath = Path.Combine(sarifDirectory, "keep-auto-advance.sarif");
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
                await SarifTools.SarifFilter("severity:high");
                _ = await SarifTools.SarifGet(limit: 2);

                var secondPage = await SarifTools.SarifGet(limit: 2);
                var secondMeta = JsonSerializer.SerializeToElement(secondPage.Meta);

                Assert.That(secondMeta.GetProperty("context").GetProperty("pagination").GetProperty("page_token").GetString(), Is.EqualTo("2"));
                Assert.That(secondMeta.GetProperty("context").GetProperty("pagination").GetProperty("page_number").GetInt32(), Is.EqualTo(2));
                Assert.That(secondMeta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString(), Is.EqualTo("3"));

                var repeatedLastPage = await SarifTools.SarifGet(limit: 2);
                var repeatedMeta = JsonSerializer.SerializeToElement(repeatedLastPage.Meta);

                Assert.That(repeatedMeta.GetProperty("context").GetProperty("pagination").GetProperty("page_token").GetString(), Is.EqualTo("2"));
                Assert.That(repeatedMeta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString(), Is.EqualTo("3"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifGet_WithExplicitPage_CanNavigateBackToFirstPage()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var resultsJson = string.Join(",", Enumerable.Range(1, 25).Select(index =>
                $$"""
                  {
                    "ruleId": "RULE-{{index}}",
                    "level": "error",
                    "message": { "text": "finding {{index}}" }
                  }
                """));

            var sarifPath = Path.Combine(sarifDirectory, "explicit-page-nav.sarif");
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
                await SarifTools.SarifFilter("severity:high");
                _ = await SarifTools.SarifGet(limit: 10);
                var page2 = await SarifTools.SarifGet(limit: 10);
                var page2Meta = JsonSerializer.SerializeToElement(page2.Meta);
                Assert.That(page2Meta.GetProperty("context").GetProperty("pagination").GetProperty("page_number").GetInt32(), Is.EqualTo(2));

                var backToPage1 = await SarifTools.SarifGet(limit: 10, page: 1);
                var backMeta = JsonSerializer.SerializeToElement(backToPage1.Meta);

                Assert.That(backMeta.GetProperty("context").GetProperty("pagination").GetProperty("page_number").GetInt32(), Is.EqualTo(1));
                Assert.That(backMeta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString(), Is.EqualTo("1"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifGet_WithScopeSet_ResetsPaginationToFirstPageByDefault()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var resultsJson = string.Join(",", Enumerable.Range(1, 25).Select(index =>
                $$"""
                  {
                    "ruleId": "RULE-{{index}}",
                    "level": "error",
                    "message": { "text": "finding {{index}}" }
                  }
                """));

            var sarifPath = Path.Combine(sarifDirectory, "set-resets-page.sarif");
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
                await SarifTools.SarifFilter("severity:high");
                _ = await SarifTools.SarifGet(limit: 10);
                _ = await SarifTools.SarifGet(limit: 10);

                await SarifTools.SarifFilter("clear");
                var resetResult = await SarifTools.SarifGet(limit: 10);
                var resetMeta = JsonSerializer.SerializeToElement(resetResult.Meta);

                Assert.That(resetMeta.GetProperty("context").GetProperty("pagination").GetProperty("page_number").GetInt32(), Is.EqualTo(1));
                Assert.That(resetMeta.GetProperty("context").GetProperty("pagination").GetProperty("page_token").GetString(), Is.EqualTo("0"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public void SarifGet_WithNegativePage_ThrowsArgumentException()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await SarifTools.SarifGet(page: -1));
        }

        [Test]
        public async Task SarifGet_WithPageToken_ReturnsNextBatchWithoutDisplayIdOverlap()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var resultsJson = string.Join(",", Enumerable.Range(1, 3).Select(index =>
                $$"""
                  {
                    "ruleId": "RULE-{{index}}",
                    "level": "error",
                    "message": { "text": "finding {{index}}" }
                  }
                """));

            var sarifPath = Path.Combine(sarifDirectory, "cursor-pagination.sarif");
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
                await SarifTools.SarifFilter("severity:high");
                var firstPage = await SarifTools.SarifGet(limit: 2);
                var firstMeta = JsonSerializer.SerializeToElement(firstPage.Meta);

                Assert.That(firstMeta.GetProperty("context").GetProperty("pagination").GetProperty("has_more").GetBoolean(), Is.True);
                Assert.That(firstMeta.GetProperty("context").GetProperty("pagination").GetProperty("next_page_token").GetString(), Is.EqualTo("2"));
                Assert.That(firstMeta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString(), Is.EqualTo("1"));
                Assert.That(firstMeta.GetProperty("context").GetProperty("aliases")[1].GetProperty("displayid").GetString(), Is.EqualTo("2"));

                var secondPage = await SarifTools.SarifGet(limit: 2, pageToken: "2");
                var secondMeta = JsonSerializer.SerializeToElement(secondPage.Meta);

                Assert.That(secondMeta.GetProperty("context").GetProperty("pagination").GetProperty("has_more").GetBoolean(), Is.False);
                Assert.That(secondMeta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString(), Is.EqualTo("3"));

                var triageResult = await SarifTools.SarifUpdate(state: "confirmed", reason: "page-token", target: "1");
                var triageText = triageResult.Content[0] as TextContentBlock;

                Assert.That(triageText, Is.Not.Null);
                Assert.That(triageText!.Text, Contains.Substring("Affected findings: **1**"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public void SarifGet_WithInvalidPageToken_ThrowsArgumentException()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await SarifTools.SarifGet(pageToken: "bad-token"));
        }

        [Test]
        public async Task SarifUpdate_WithDisplayAliasTarget_ResolvesToUnderlyingFindingId()
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
                await SarifTools.SarifFilter("severity:high");
                var getResult = await SarifTools.SarifGet(limit: 10);
                var stateContext = JsonSerializer.SerializeToElement(getResult.Meta);
                var alias = stateContext
                    .GetProperty("context")
                    .GetProperty("aliases")[0]
                    .GetProperty("displayid")
                    .GetString();

                Assert.That(alias, Is.Not.Null.And.Not.Empty);

                var triageResult = await SarifTools.SarifUpdate(
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
            SetInternalSarifToolsProperty("PromptAssembly", null);
            SarifTools.SetDiscoveredSarifFiles(Array.Empty<string>());
            SarifTools.SetLocalUiBaseUrl(string.Empty);
            SarifTools.SetWorkspaceRoot(Directory.GetCurrentDirectory());
        }

        [Test]
        public async Task SarifGet_WithFilterLifecycle_ReturnsScopedEnvelopeAndMetrics()
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
                await SarifTools.SarifFilter("severity:high");
                var setResult = await SarifTools.SarifGet(limit: 10);
                var setMeta = JsonSerializer.SerializeToElement(setResult.Meta);

                Assert.That(setMeta.GetProperty("context").GetProperty("active_scope").GetProperty("severity").GetString(), Is.EqualTo("high"));
                Assert.That(setMeta.GetProperty("context").GetProperty("metrics").GetProperty("total_in_scope").GetInt32(), Is.EqualTo(1));
                Assert.That(setMeta.GetProperty("pause").GetBoolean(), Is.True);
                Assert.That(setMeta.GetProperty("next_step").GetString(), Is.EqualTo("sarif_review"));
                Assert.That(((TextContentBlock)setResult.Content[0]).Text, Contains.Substring("## ??? SARIF Findings (Page 1 of 1)"));
                Assert.That(((TextContentBlock)setResult.Content[0]).Text, Contains.Substring(SarifTools.StateContextDelimiter));

                await SarifTools.SarifFilter("severity:high rule:RULE-HIGH");
                var refineResult = await SarifTools.SarifGet(limit: 10);
                var refineMeta = JsonSerializer.SerializeToElement(refineResult.Meta);

                Assert.That(refineMeta.GetProperty("context").GetProperty("active_scope").GetProperty("severity").GetString(), Is.EqualTo("high"));
                Assert.That(refineMeta.GetProperty("context").GetProperty("active_scope").GetProperty("rule").GetString(), Is.EqualTo("RULE-HIGH"));

                await SarifTools.SarifFilter("clear");
                var clearResult = await SarifTools.SarifGet(limit: 10);
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
        public async Task SarifGet_WithClearedScope_ShowsAllFindings()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "set-no-filter.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-ONE",
                      "level": "error",
                      "message": { "text": "one" }
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
                await SarifTools.SarifFilter("clear");

                var result = await SarifTools.SarifGet(limit: 10);
                var meta = JsonSerializer.SerializeToElement(result.Meta);

                Assert.That(meta.GetProperty("context").GetProperty("active_scope").EnumerateObject().Count(), Is.EqualTo(0));
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
                var result = await SarifTools.SarifGet(limit: 100);
                var meta = JsonSerializer.SerializeToElement(result.Meta);

                Assert.That(meta.GetProperty("context").GetProperty("metrics").GetProperty("returned_in_batch").GetInt32(), Is.EqualTo(25));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifUpdate_WithScopeTarget_AppliesDecisionToActiveScope()
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
                await SarifTools.SarifFilter("severity:high");
                await SarifTools.SarifGet(limit: 10);

                var triageResult = await SarifTools.SarifUpdate(
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
        public async Task SarifUpdate_WithInvalidState_ReturnsValidationError()
        {
            var result = await SarifTools.SarifUpdate(state: "tp", reason: "invalid", target: "scope");
            var text = (result.Content[0] as TextContentBlock)?.Text;

            Assert.That(text, Is.Not.Null);
            Assert.That(text, Does.Contain("`state` must be one of: confirmed, false_positive, test_code, wont_fix, mitigated."));
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
        public void McpToolExposure_ContainsStatefulTools()
        {
            var toolMethods = typeof(SarifTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Length > 0)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.That(toolMethods, Is.EqualTo(new[] { "SarifFilter", "SarifGet", "SarifReview", "SarifSync", "SarifUpdate" }));
        }

        [Test]
        public async Task SarifGet_WhenScopeSetTwice_PreservesGlobalDisplayIdSequence()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "displayid-reset.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-A",
                      "level": "error",
                      "message": { "text": "finding A" }
                    },
                    {
                      "ruleId": "RULE-B",
                      "level": "warning",
                      "message": { "text": "finding B" }
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
                await SarifTools.SarifFilter("severity:high");
                var firstGet = await SarifTools.SarifGet(limit: 10);
                var firstMeta = JsonSerializer.SerializeToElement(firstGet.Meta);
                var firstAlias = firstMeta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString();

                Assert.That(firstAlias, Is.EqualTo("1"));

                await SarifTools.SarifFilter("severity:medium");
                var secondGet = await SarifTools.SarifGet(limit: 10);
                var secondMeta = JsonSerializer.SerializeToElement(secondGet.Meta);
                var secondAlias = secondMeta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString();

                Assert.That(secondAlias, Is.EqualTo("2"), "DisplayId should stay globally unique across scope changes");
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifReview_WithPromptAssembly_ReturnsSystemDirectiveAndEvidence()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var promptsDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "base");
            Directory.CreateDirectory(promptsDir);
            File.WriteAllText(Path.Combine(promptsDir, "core-directive.md"), "# Test Core Directive");
            File.WriteAllText(Path.Combine(promptsDir, "output-format.md"), "# Test Output Format");
            var categoriesDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "categories");
            Directory.CreateDirectory(categoriesDir);
            File.WriteAllText(Path.Combine(categoriesDir, "default-sast.md"), "# Test Default SAST");

            var sarifPath = Path.Combine(sarifDirectory, "review-directive.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-REVIEW-DIRECTIVE",
                      "level": "error",
                      "message": { "text": "review directive finding" }
                    }
                  ]
                }
              ]
            }
            """);

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { sarifPath });
            SetInternalSarifToolsProperty("PromptAssembly", new PromptAssemblyService(
                Path.Combine(workspace, ".sarif", "sarifintown-prompts")));

            try
            {
                var getResult = await SarifTools.SarifGet(limit: 10);
                var meta = JsonSerializer.SerializeToElement(getResult.Meta);
                var displayId = meta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString();

                var reviewResult = await SarifTools.SarifReview(target: displayId!);
                var text = ((TextContentBlock)reviewResult.Content[0]).Text;

                Assert.That(text, Contains.Substring("## Evidence for Review"));
                Assert.That(text, Contains.Substring("<system_directive>"));
                Assert.That(text, Contains.Substring("# Test Core Directive"));
                Assert.That(text, Contains.Substring("sarif_update"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifGet_DoesNotIncludePromptOrEvidenceInResponse()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var promptsDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "base");
            Directory.CreateDirectory(promptsDir);
            File.WriteAllText(Path.Combine(promptsDir, "core-directive.md"), "# Test Core Directive");
            File.WriteAllText(Path.Combine(promptsDir, "output-format.md"), "# Test Output Format");
            var categoriesDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "categories");
            Directory.CreateDirectory(categoriesDir);
            File.WriteAllText(Path.Combine(categoriesDir, "default-sast.md"), "# Test Default SAST");

            var sarifPath = Path.Combine(sarifDirectory, "no-prompt-in-get.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-NOPROMPTGET",
                      "level": "error",
                      "message": { "text": "no prompt in get" }
                    }
                  ]
                }
              ]
            }
            """);

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { sarifPath });
            SetInternalSarifToolsProperty("PromptAssembly", new PromptAssemblyService(
                Path.Combine(workspace, ".sarif", "sarifintown-prompts")));

            try
            {
                var result = await SarifTools.SarifGet();
                var text = ((TextContentBlock)result.Content[0]).Text;

                Assert.That(text, Does.Not.Contain("DEBUG: Assembled Triage Prompt"));
                Assert.That(text, Does.Not.Contain("Triage Analysis Instructions"));
                Assert.That(text, Does.Not.Contain("# Test Core Directive"));
                Assert.That(text, Contains.Substring("## SARIF Findings Index"));
                Assert.That(text, Contains.Substring("sarif_review"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifUpdate_WithLlmReasoning_RecordsAsAiDecision()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "ai-triage.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-AI-TRIAGE",
                      "level": "error",
                      "message": { "text": "ai triage finding" }
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
                var getResult = await SarifTools.SarifGet(limit: 10);
                var meta = JsonSerializer.SerializeToElement(getResult.Meta);
                var displayId = meta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString();

                var updateResult = await SarifTools.SarifUpdate(
                    target: displayId!,
                    state: "false_positive",
                    reason: "Test code pattern",
                    llmReasoning: "I analyzed the code flow and determined this is not exploitable.");
                var text = ((TextContentBlock)updateResult.Content[0]).Text;

                Assert.That(text, Contains.Substring("## SARIF Scoped Review"));
                Assert.That(text, Contains.Substring("Ledger entries written:"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifReview_WithoutPromptAssembly_StillReturnsEvidenceStructure()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "review-no-assembly.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-REVIEW-NOASSEMBLY",
                      "level": "error",
                      "message": { "text": "review no assembly" }
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
                var getResult = await SarifTools.SarifGet(limit: 10);
                var meta = JsonSerializer.SerializeToElement(getResult.Meta);
                var displayId = meta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString();

                var reviewResult = await SarifTools.SarifReview(target: displayId!);
                var text = ((TextContentBlock)reviewResult.Content[0]).Text;

                Assert.That(text, Contains.Substring("## Evidence for Review"));
                Assert.That(text, Contains.Substring("sarif_update"));
                Assert.That(text, Does.Not.Contain("DEBUG: Assembled Triage Prompt"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifUpdate_WithSingleTarget_ShowsModifiedFindingsSection()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "modified-detail.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-DETAIL",
                      "level": "error",
                      "message": { "text": "detail finding" }
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
                await SarifTools.SarifFilter("severity:high");
                var getResult = await SarifTools.SarifGet(limit: 10);
                var meta = JsonSerializer.SerializeToElement(getResult.Meta);
                var displayId = meta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString();

                var triageResult = await SarifTools.SarifUpdate(
                    state: "false_positive",
                    reason: "test code only",
                    target: displayId!);

                var triageText = triageResult.Content[0] as TextContentBlock;
                Assert.That(triageText, Is.Not.Null);
                Assert.That(triageText!.Text, Contains.Substring("### Modified Findings"));
                Assert.That(triageText.Text, Contains.Substring($"`{displayId}`"));
                Assert.That(triageText.Text, Contains.Substring("`FP`"));
                Assert.That(triageText.Text, Contains.Substring("Original reasoning: test code only"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifUpdate_WithPromptAssemblyAndDataFlow_IncludesEvidenceWithoutPromptProvenance()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sourceDirectory = Path.Combine(workspace, "src");
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(Path.Combine(sourceDirectory, "flow.cs"), "public class Flow { public void Execute(string user) { var sql = \"select \" + user; } }");

            var promptsDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "base");
            Directory.CreateDirectory(promptsDir);
            File.WriteAllText(Path.Combine(promptsDir, "core-directive.md"), "# Test Core Directive");
            File.WriteAllText(Path.Combine(promptsDir, "output-format.md"), "# Test Output Format");
            var categoriesDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "categories");
            Directory.CreateDirectory(categoriesDir);
            File.WriteAllText(Path.Combine(categoriesDir, "sast-sqli.md"), "# Test SQLI Category");

            var sarifPath = Path.Combine(sarifDirectory, "triage-evidence.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "SQLI-RULE",
                      "level": "error",
                      "message": { "text": "user input reaches SQL sink" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/flow.cs" },
                            "region": { "startLine": 1 }
                          }
                        }
                      ],
                      "codeFlows": [
                        {
                          "threadFlows": [
                            {
                              "locations": [
                                {
                                  "location": {
                                    "message": { "text": "source" },
                                    "physicalLocation": {
                                      "artifactLocation": { "uri": "src/flow.cs" },
                                      "region": { "startLine": 1 }
                                    }
                                  }
                                }
                              ]
                            }
                          ]
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
            SetInternalSarifToolsProperty("PromptAssembly", new PromptAssemblyService(
                Path.Combine(workspace, ".sarif", "sarifintown-prompts")));

            try
            {
                await SarifTools.SarifFilter("severity:high");
                var getResult = await SarifTools.SarifGet(limit: 10);
                var meta = JsonSerializer.SerializeToElement(getResult.Meta);
                var displayId = meta.GetProperty("context").GetProperty("aliases")[0].GetProperty("displayid").GetString();

                var triageResult = await SarifTools.SarifUpdate(
                    state: "confirmed",
                    reason: "confirmed-from-flow",
                    target: displayId!);

                var triageText = ((TextContentBlock)triageResult.Content[0]).Text;
                Assert.That(triageText, Contains.Substring("### Decision Evidence"));
                Assert.That(triageText, Contains.Substring("##### Data Flow Used"));
                Assert.That(triageText, Contains.Substring("Original reasoning: confirmed-from-flow"));
                Assert.That(triageText, Does.Not.Contain("##### Prompt Provenance"));
                Assert.That(triageText, Does.Not.Contain("# Test Core Directive"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifGet_WithPromptAssembly_DoesNotIncludePromptContent()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var promptsDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "base");
            Directory.CreateDirectory(promptsDir);
            File.WriteAllText(Path.Combine(promptsDir, "core-directive.md"), "# Core Directive");
            File.WriteAllText(Path.Combine(promptsDir, "output-format.md"), "# Output Format");
            var categoriesDir = Path.Combine(workspace, ".sarif", "sarifintown-prompts", "categories");
            Directory.CreateDirectory(categoriesDir);
            File.WriteAllText(Path.Combine(categoriesDir, "default-sast.md"), "# Default SAST Category");

            var sarifPath = Path.Combine(sarifDirectory, "evidence-guidance.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-GUIDANCE",
                      "level": "error",
                      "message": { "text": "guidance test" }
                    }
                  ]
                }
              ]
            }
            """);

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { sarifPath });
            SetInternalSarifToolsProperty("PromptAssembly", new PromptAssemblyService(
                Path.Combine(workspace, ".sarif", "sarifintown-prompts")));

            try
            {
                var result = await SarifTools.SarifGet();
                var text = ((TextContentBlock)result.Content[0]).Text;

                Assert.That(text, Does.Not.Contain("### Triage Analysis Instructions"));
                Assert.That(text, Does.Not.Contain("# Core Directive"));
                Assert.That(text, Does.Not.Contain("DEBUG: Assembled Triage Prompt"));
                Assert.That(text, Contains.Substring("sarif_review"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task SarifGet_WithoutPromptAssembly_OmitsTriageGuidance()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "no-prompt.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-NOPROMPT",
                      "level": "error",
                      "message": { "text": "no prompt test" }
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
                var result = await SarifTools.SarifGet();
                var text = ((TextContentBlock)result.Content[0]).Text;

                Assert.That(text, Does.Not.Contain("### Triage Guidance Per Finding"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        private static void SetInternalSarifToolsProperty(string propertyName, object? value)
        {
            var property = typeof(SarifTools).GetProperty(propertyName, BindingFlags.Static | BindingFlags.NonPublic);
            property!.SetValue(null, value);
        }
    }
}
