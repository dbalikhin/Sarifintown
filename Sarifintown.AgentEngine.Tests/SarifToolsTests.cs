using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
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

                // Fallback to actual file read if not in dictionary
                if (File.Exists(relativePath))
                {
                    return File.ReadAllTextAsync(relativePath);
                }
                throw new FileNotFoundException($"File not found: {relativePath}");
            }
        }

        [Test]
        public async Task ManageTriage_WithQueryAction_ReturnsStatusAndPrioritizedFindingsInSingleResponse()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "facade-query.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-QUERY",
                      "level": "error",
                      "message": { "text": "query me" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Query.cs" },
                            "region": { "startLine": 11 }
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
                var queryResult = await SarifTools.manage_triage("query", filters: "{\"limit\":1}");
                var queryText = queryResult.Content[0] as TextContentBlock;

                Assert.That(queryText, Is.Not.Null);
                Assert.That(queryText!.Text, Contains.Substring("## SARIF Triage Query"));
                Assert.That(queryText.Text, Contains.Substring("Total findings"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task ManageTriage_WithDecideActionAndFindingIdsFilter_AppliesMultiTargetDecision()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "facade-decide-ids.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-IDS-1",
                      "level": "error",
                      "message": { "text": "first" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Ids1.cs" },
                            "region": { "startLine": 5 }
                          }
                        }
                      ]
                    },
                    {
                      "ruleId": "RULE-IDS-2",
                      "level": "warning",
                      "message": { "text": "second" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Ids2.cs" },
                            "region": { "startLine": 7 }
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
                var listJson = await SarifTools.TriageList(limit: 2);
                var listPayload = JsonSerializer.Deserialize<List<JsonElement>>(listJson);
                var findingIds = string.Join(",", listPayload!.Select(item => item.GetProperty("FindingId").GetString()));

                var decideResult = await SarifTools.manage_triage(
                    "decide",
                    state: "FP",
                    reason: "campaign",
                    filters: $"{{\"findingIds\":\"{findingIds}\"}}");

                var decideText = decideResult.Content[0] as TextContentBlock;
                Assert.That(decideText, Is.Not.Null);
                Assert.That(decideText!.Text, Contains.Substring("## Bulk Triage Result"));
                Assert.That(decideText.Text, Contains.Substring("Affected Findings: **2**"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task TriageList_WithLatestPerToolPreload_IncludesOnlyNewestFilePerTool()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var olderToolAPath = Path.Combine(sarifDirectory, "tool-a-older.sarif");
            File.WriteAllText(olderToolAPath, """
            {
              "runs": [
                {
                  "tool": { "driver": { "name": "ToolA" } },
                  "results": [
                    {
                      "ruleId": "RULE-OLD",
                      "level": "warning",
                      "message": { "text": "old" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/old.cs" },
                            "region": { "startLine": 1 }
                          }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

            var latestToolAPath = Path.Combine(sarifDirectory, "tool-a-latest.sarif");
            File.WriteAllText(latestToolAPath, """
            {
              "runs": [
                {
                  "tool": { "driver": { "name": "ToolA" } },
                  "results": [
                    {
                      "ruleId": "RULE-NEW",
                      "level": "error",
                      "message": { "text": "new" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/new.cs" },
                            "region": { "startLine": 2 }
                          }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

            var toolBPath = Path.Combine(sarifDirectory, "tool-b.sarif");
            File.WriteAllText(toolBPath, """
            {
              "runs": [
                {
                  "tool": { "driver": { "name": "ToolB" } },
                  "results": [
                    {
                      "ruleId": "RULE-B",
                      "level": "warning",
                      "message": { "text": "b" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/b.cs" },
                            "region": { "startLine": 3 }
                          }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

            var baseline = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(olderToolAPath, baseline.AddMinutes(-5));
            File.SetLastWriteTimeUtc(latestToolAPath, baseline.AddMinutes(-1));
            File.SetLastWriteTimeUtc(toolBPath, baseline.AddMinutes(-2));

            SarifTools.SetWorkspaceRoot(workspace);
            SarifTools.SetDiscoveredSarifFiles(new[] { olderToolAPath, latestToolAPath, toolBPath });

            try
            {
                var responseJson = await SarifTools.TriageList(limit: 10);
                var payload = JsonSerializer.Deserialize<List<JsonElement>>(responseJson);
                var ruleNames = payload!.Select(item => item.GetProperty("RuleName").GetString()).ToList();

                Assert.That(ruleNames, Is.EqualTo(new[] { "RULE-NEW", "RULE-B" }));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task ManageTriage_WithDecideAndBulkActions_ReturnsMarkdownSummaries()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "facade-decide-bulk.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-FACADE-DECIDE",
                      "level": "error",
                      "message": { "text": "triage me" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Facade.cs" },
                            "region": { "startLine": 9 }
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
                var listJson = await SarifTools.TriageList(limit: 1);
                var listPayload = JsonSerializer.Deserialize<List<JsonElement>>(listJson);
                var findingId = listPayload![0].GetProperty("FindingId").GetString();

                var decideResult = await SarifTools.manage_triage("decide", findingId!, state: "TP", reason: "validated");
                var decideText = decideResult.Content[0] as TextContentBlock;

                Assert.That(decideText, Is.Not.Null);
                Assert.That(decideText!.Text, Contains.Substring("## Triage Decision Result"));
                Assert.That(decideText.Text, Does.Not.Contain("```json"));

                var bulkResult = await SarifTools.manage_triage("bulk_decide", state: "FP", reason: "bulk-noise", filters: "{\"rule\":\"RULE-FACADE-DECIDE\",\"dryRun\":true}");
                var bulkText = bulkResult.Content[0] as TextContentBlock;

                Assert.That(bulkText, Is.Not.Null);
                Assert.That(bulkText!.Text, Contains.Substring("## Bulk Triage Result"));
                Assert.That(bulkText.Text, Does.Not.Contain("```json"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [TestCase("true positive", "TP")]
        [TestCase("false positive", "FP")]
        public async Task Triage_WithNaturalLanguageState_MapsToExpectedState(string inputState, string expectedState)
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "triage-state-alias.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-ALIAS",
                      "level": "error",
                      "message": { "text": "Alias test" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/a.cs" },
                            "region": { "startLine": 7 }
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
            SarifTools.SetLocalUiBaseUrl("http://127.0.0.1:54321");

            try
            {
                var listJson = await SarifTools.TriageList(limit: 1);
                var listPayload = JsonSerializer.Deserialize<List<JsonElement>>(listJson);
                var findingId = listPayload![0].GetProperty("FindingId").GetString();

                var triageJson = await SarifTools.Triage(findingId!, inputState, "confirmed", "AI");
                var triagePayload = JsonSerializer.Deserialize<JsonElement>(triageJson);

                Assert.That(triagePayload.GetProperty("State").GetString(), Is.EqualTo(expectedState));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task TriageInspect_WithLineWindowConcatenated_MergesNearbySteps()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            var sourceDirectory = Path.Combine(workspace, "src");
            Directory.CreateDirectory(sarifDirectory);
            Directory.CreateDirectory(sourceDirectory);

            var sourcePath = Path.Combine(sourceDirectory, "Flow.cs");
            File.WriteAllText(sourcePath, string.Join("\n", Enumerable.Range(1, 40).Select(index => $"line {index}")));

            var sarifPath = Path.Combine(sarifDirectory, "inspect.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-FLOW",
                      "message": { "text": "flow" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Flow.cs" },
                            "region": { "startLine": 10 }
                          }
                        }
                      ],
                      "codeFlows": [
                        {
                          "threadFlows": [
                            {
                              "locations": [
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/Flow.cs" }, "region": { "startLine": 10 } } } },
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/Flow.cs" }, "region": { "startLine": 14 } } } },
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/Flow.cs" }, "region": { "startLine": 30 } } } }
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

            try
            {
                var listJson = await SarifTools.TriageList(limit: 1);
                var listPayload = JsonSerializer.Deserialize<List<JsonElement>>(listJson);
                var findingId = listPayload![0].GetProperty("FindingId").GetString();

                var inspectJson = await SarifTools.TriageInspect(findingId!, "line-window-concatenated");
                var inspectPayload = JsonSerializer.Deserialize<JsonElement>(inspectJson);

                Assert.That(inspectPayload.GetProperty("DataFlowEvidenceMode").GetString(), Is.EqualTo("line-window-concatenated"));
                Assert.That(inspectPayload.GetProperty("DataFlowEvidenceBlocks").GetArrayLength(), Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task TriageInspect_WithLineWindowStrict_UsesPerStepBlocks()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            var sourceDirectory = Path.Combine(workspace, "src");
            Directory.CreateDirectory(sarifDirectory);
            Directory.CreateDirectory(sourceDirectory);

            var sourcePath = Path.Combine(sourceDirectory, "Strict.cs");
            File.WriteAllText(sourcePath, string.Join("\n", Enumerable.Range(1, 20).Select(index => $"line {index}")));

            var sarifPath = Path.Combine(sarifDirectory, "strict-inspect.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-STRICT",
                      "message": { "text": "flow" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Strict.cs" },
                            "region": { "startLine": 4 }
                          }
                        }
                      ],
                      "codeFlows": [
                        {
                          "threadFlows": [
                            {
                              "locations": [
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/Strict.cs" }, "region": { "startLine": 4 } } } },
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/Strict.cs" }, "region": { "startLine": 9 } } } }
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

            try
            {
                var listJson = await SarifTools.TriageList(limit: 1);
                var listPayload = JsonSerializer.Deserialize<List<JsonElement>>(listJson);
                var findingId = listPayload![0].GetProperty("FindingId").GetString();

                var inspectJson = await SarifTools.TriageInspect(findingId!, "line-window-strict");
                var inspectPayload = JsonSerializer.Deserialize<JsonElement>(inspectJson);

                Assert.That(inspectPayload.GetProperty("DataFlowEvidenceMode").GetString(), Is.EqualTo("line-window-strict"));
                Assert.That(inspectPayload.GetProperty("DataFlowEvidenceBlocks").GetArrayLength(), Is.EqualTo(2));
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

            public Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine)
            {
                Interlocked.Increment(ref _extractMethodCallCount);
                return Task.FromResult("extracted_code_snippet");
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
        public async Task TriageInspect_WithSharedSnippetCache_ReusesExtractedMethodAcrossCalls()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            var sourceDirectory = Path.Combine(workspace, "src");
            Directory.CreateDirectory(sarifDirectory);
            Directory.CreateDirectory(sourceDirectory);

            var sourcePath = Path.Combine(sourceDirectory, "Cached.cs");
            File.WriteAllText(sourcePath, "class Cached { void Run() { } }");

            var sarifPath = Path.Combine(sarifDirectory, "cached-inspect.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-CACHED",
                      "message": { "text": "flow" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Cached.cs" },
                            "region": { "startLine": 1 }
                          }
                        }
                      ],
                      "codeFlows": [
                        {
                          "threadFlows": [
                            {
                              "locations": [
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/Cached.cs" }, "region": { "startLine": 1, "endLine": 1 } } } }
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
            SetInternalSarifToolsProperty("SnippetCache", CreateInternalTypeInstance("Sarifintown.AgentEngine.SnippetCacheService"));

            try
            {
                var listJson = await SarifTools.TriageList(limit: 1);
                var listPayload = JsonSerializer.Deserialize<List<JsonElement>>(listJson);
                var findingId = listPayload![0].GetProperty("FindingId").GetString();

                var firstInspect = await SarifTools.TriageInspect(findingId!);
                var secondInspect = await SarifTools.TriageInspect(findingId!);

                Assert.That(firstInspect, Contains.Substring("extracted_code_snippet"));
                Assert.That(secondInspect, Contains.Substring("extracted_code_snippet"));
                Assert.That(FakeTreeSitterEngine.ExtractMethodCallCount, Is.EqualTo(1));
            }
            finally
            {
                SetInternalSarifToolsProperty("SnippetCache", null);
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task TriageStatus_WithTriageState_ReturnsAggregatedCounts()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "status.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-HIGH",
                      "level": "error",
                      "message": { "text": "High issue" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/one.cs" },
                            "region": { "startLine": 10 }
                          }
                        }
                      ]
                    },
                    {
                      "ruleId": "RULE-MED",
                      "level": "warning",
                      "message": { "text": "Medium issue" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/two.cs" },
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
                var findingsJson = await SarifTools.TriageList(limit: 5);
                var findings = JsonSerializer.Deserialize<List<JsonElement>>(findingsJson);
                var findingId = findings![0].GetProperty("FindingId").GetString();

                var triagePath = Path.Combine(sarifDirectory, "triage.json");
                File.WriteAllText(triagePath, JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    entries = new[]
                    {
                        new
                        {
                            findingId,
                            state = "FP",
                            reason = "known noise",
                            author = "AI",
                            updatedUtc = "2025-01-01T00:00:00Z"
                        }
                    }
                }));

                var responseJson = await SarifTools.TriageStatus();
                var payload = JsonSerializer.Deserialize<JsonElement>(responseJson);

                Assert.That(payload.GetProperty("TotalFindings").GetInt32(), Is.EqualTo(2));
                Assert.That(payload.GetProperty("TriagedCount").GetInt32(), Is.EqualTo(1));
                Assert.That(payload.GetProperty("OpenCount").GetInt32(), Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task TriageList_WithFilters_ReturnsPrioritizedItems()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "list.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "SQLInjection",
                      "level": "error",
                      "message": { "text": "Critical path" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/UserController.cs" },
                            "region": { "startLine": 15 }
                          }
                        }
                      ],
                      "codeFlows": [
                        {
                          "threadFlows": [
                            {
                              "locations": [
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/UserController.cs" }, "region": { "startLine": 15 } } } },
                                { "location": { "physicalLocation": { "artifactLocation": { "uri": "src/UserController.cs" }, "region": { "startLine": 32 } } } }
                              ]
                            }
                          ]
                        }
                      ]
                    },
                    {
                      "ruleId": "CA1031",
                      "level": "warning",
                      "message": { "text": "Lower priority" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "tests/Ignore.cs" },
                            "region": { "startLine": 8 }
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
                var responseJson = await SarifTools.TriageList(severity: "High", file: "*UserController.cs", limit: 5);
                var payload = JsonSerializer.Deserialize<List<JsonElement>>(responseJson);

                Assert.That(payload, Is.Not.Null);
                Assert.That(payload!.Count, Is.EqualTo(1));
                Assert.That(payload[0].GetProperty("RuleName").GetString(), Is.EqualTo("SQLInjection"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task Triage_And_TriageBulkDryRun_UpdateAndPreviewState()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "triage.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-ONE",
                      "level": "error",
                      "message": { "text": "One" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/a.cs" },
                            "region": { "startLine": 7 }
                          }
                        }
                      ]
                    },
                    {
                      "ruleId": "RULE-TWO",
                      "level": "warning",
                      "message": { "text": "Two" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/b.cs" },
                            "region": { "startLine": 9 }
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
                var listJson = await SarifTools.TriageList(limit: 1);
                var listPayload = JsonSerializer.Deserialize<List<JsonElement>>(listJson);
                var findingId = listPayload![0].GetProperty("FindingId").GetString();

                var triageJson = await SarifTools.Triage(findingId!, "TP", "validated", "User");
                var triagePayload = JsonSerializer.Deserialize<JsonElement>(triageJson);
                Assert.That(triagePayload.GetProperty("Success").GetBoolean(), Is.True);

                var triageFileJson = File.ReadAllText(Path.Combine(sarifDirectory, "triage.json"));
                Assert.That(triageFileJson, Does.Contain("validated"));

                var dryRunJson = await SarifTools.TriageBulk("FP", "noise", severity: "Medium", dryRun: true);
                var dryRunPayload = JsonSerializer.Deserialize<JsonElement>(dryRunJson);
                Assert.That(dryRunPayload.GetProperty("DryRun").GetBoolean(), Is.True);
                Assert.That(dryRunPayload.GetProperty("AffectedCount").GetInt32(), Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public void GenerateAnalysisReport_WithValidData_CreatesMarkdownFile()
        {
            // Arrange
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
                // Act
                var result = SarifTools.GenerateAnalysisReport(resultId, extractedFlowData, outputPath);

                // Assert
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
        public async Task TriageStatusGuided_ReturnsDirectiveAndNextStepMetadata()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "guided-status.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-GUIDED",
                      "level": "warning",
                      "message": { "text": "Guided status" }
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
                var result = await SarifTools.TriageStatusGuided();
                var payload = JsonSerializer.Deserialize<JsonElement>(result);

                Assert.That(payload.GetProperty("protocol").GetString(), Is.EqualTo("sarifintown.guided.v1"));
                Assert.That(payload.GetProperty("next_step").GetProperty("tool").GetString(), Is.EqualTo("manage_triage"));
                Assert.That(payload.GetProperty("pause").GetProperty("required").GetBoolean(), Is.True);
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task TriageListGuided_ReturnsTableAndInspectNextStep()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "guided-list.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-LIST",
                      "level": "error",
                      "message": { "text": "Guided list" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/list.cs" },
                            "region": { "startLine": 12 }
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
                var result = await SarifTools.TriageListGuided(limit: 5);
                var payload = JsonSerializer.Deserialize<JsonElement>(result);

                Assert.That(payload.GetProperty("next_step").GetProperty("tool").GetString(), Is.EqualTo("manage_triage"));
                Assert.That(payload.GetProperty("markdown").GetString(), Contains.Substring("| FindingId | Severity | State | Rule | Location |"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task TriageInspectGuided_WithUnknownFinding_ReturnsListRecoveryStep()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "guided-inspect.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-INSPECT",
                      "level": "warning",
                      "message": { "text": "Guided inspect" }
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
                var result = await SarifTools.TriageInspectGuided("missing-id");
                var payload = JsonSerializer.Deserialize<JsonElement>(result);

                Assert.That(payload.GetProperty("next_step").GetProperty("tool").GetString(), Is.EqualTo("manage_triage"));
                Assert.That(payload.GetProperty("markdown").GetString(), Contains.Substring("Finding Not Found"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public void ResolveInteractiveSurface_WithVsCodeHostHint_ReturnsUiMode()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "Visual Studio Code");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("mode").GetString(), Is.EqualTo("ide-ui"));
            Assert.That(payload.GetProperty("host_family").GetString(), Is.EqualTo("vscode-family"));
        }

        [Test]
        public void ResolveInteractiveSurface_WithCursorHostHint_ReturnsUiUri()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "Cursor");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("uri").GetString(), Is.EqualTo("ui://sarifintown/mcp/dashboard"));
            Assert.That(payload.GetProperty("bridge").GetProperty("transport").GetString(), Is.EqualTo("postMessage"));
            Assert.That(payload.GetProperty("bridge").GetProperty("channel").GetString(), Is.EqualTo("sarifintown.mcp.v1"));
            Assert.That(payload.GetProperty("local_http_ui").GetProperty("available").GetBoolean(), Is.False);
        }

        [Test]
        public void ResolveInteractiveSurface_WithLocalUiBaseUrl_ReturnsLocalhostFallbackUri()
        {
            // Arrange
            SarifTools.SetLocalUiBaseUrl("http://127.0.0.1:54321");

            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "Visual Studio Code");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("local_http_ui").GetProperty("available").GetBoolean(), Is.True);
            Assert.That(payload.GetProperty("local_http_ui").GetProperty("uri").GetString(), Is.EqualTo("http://127.0.0.1:54321/mcp/dashboard"));
        }

        [Test]
        public void ResolveInteractiveSurface_WithClaudeHostHint_ReturnsSpectreTui()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "Claude Code");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("tui").GetProperty("library").GetString(), Is.EqualTo("Spectre.Console"));
            Assert.That(payload.GetProperty("host_family").GetString(), Is.EqualTo("terminal-family"));
        }

        [Test]
        public void ResolveInteractiveSurface_WithWindsurfHostHint_ReturnsIdeUiMode()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "Windsurf");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("mode").GetString(), Is.EqualTo("ide-ui"));
        }

        [Test]
        public void ResolveInteractiveSurface_WithRiderHostHint_ReturnsIdeUiMode()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "JetBrains Rider");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("mode").GetString(), Is.EqualTo("ide-ui"));
            Assert.That(payload.GetProperty("host_family").GetString(), Is.EqualTo("jetbrains-family"));
        }

        [Test]
        public void ResolveInteractiveSurface_WithPowerShellHostHint_ReturnsCliTuiMode()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "PowerShell");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("mode").GetString(), Is.EqualTo("cli-tui"));
        }

        [Test]
        public void ResolveInteractiveSurface_WithUnknownHostHint_DoesNotSetFallbackUsed()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "Some Future MCP Host");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("fallback_used").GetBoolean(), Is.False);
        }

        [Test]
        public void ResolveInteractiveSurface_WithEmptyHint_UsesFallback()
        {
            // Act
            var result = SarifTools.ResolveInteractiveSurface(null!, hostHint: "");
            var payload = JsonSerializer.Deserialize<JsonElement>(result);

            // Assert
            Assert.That(payload.GetProperty("fallback_used").GetBoolean(), Is.True);
        }

        [Test]
        public async Task ManageTriage_WithGuidedStatusAction_ReturnsGuidedPayload()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "guided-facade.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-FACADE",
                      "level": "warning",
                      "message": { "text": "facade" }
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
                var result = await SarifTools.manage_triage("status", filters: "{\"guided\":true}");
                Assert.That(result.Content, Is.Not.Null);
                Assert.That(result.Content.Count, Is.EqualTo(1));

                var textBlock = result.Content[0] as TextContentBlock;
                Assert.That(textBlock, Is.Not.Null);
                Assert.That(textBlock!.Text, Contains.Substring("[INSTRUCTIONS FOR LLM]"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task ManageTriage_WithStatusAction_ReturnsRealPayloadWithoutDummyContent()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "status-facade.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "RULE-STATUS",
                      "level": "warning",
                      "message": { "text": "status" }
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
                var result = await SarifTools.manage_triage("status");
                var textBlock = result.Content[0] as TextContentBlock;

                Assert.That(textBlock, Is.Not.Null);
                Assert.That(textBlock!.Text, Does.Not.Contain("Dummy payload"));
                Assert.That(textBlock.Text, Contains.Substring("## SARIF Triage Status"));
                Assert.That(textBlock.Text, Does.Not.Contain("```json"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public async Task ManageTriage_WithSqlIssuesAction_FiltersToSqlRelatedRules()
        {
            var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var sarifDirectory = Path.Combine(workspace, ".sarif");
            Directory.CreateDirectory(sarifDirectory);

            var sarifPath = Path.Combine(sarifDirectory, "sql-issues.sarif");
            File.WriteAllText(sarifPath, """
            {
              "runs": [
                {
                  "results": [
                    {
                      "ruleId": "csharp/Sqli",
                      "level": "error",
                      "message": { "text": "SQL injection" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Api.cs" },
                            "region": { "startLine": 12 }
                          }
                        }
                      ]
                    },
                    {
                      "ruleId": "CA1031",
                      "level": "warning",
                      "message": { "text": "catch all" },
                      "locations": [
                        {
                          "physicalLocation": {
                            "artifactLocation": { "uri": "src/Api.cs" },
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
                var result = await SarifTools.manage_triage("sql_issues", filters: "{\"limit\":10}");
                var textBlock = result.Content[0] as TextContentBlock;

                Assert.That(textBlock, Is.Not.Null);
                Assert.That(textBlock!.Text, Contains.Substring("csharp/Sqli"));
                Assert.That(textBlock.Text, Does.Not.Contain("CA1031"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Test]
        public void BuildUiResourceUri_WithDynamicLocalUiBaseUrl_UsesAssignedPort()
        {
            SarifTools.SetLocalUiBaseUrl("http://127.0.0.1:54321");

            var method = typeof(SarifTools)
                .GetMethod("BuildUiResourceUri", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            var value = method!.Invoke(null, new object[] { "triage", "status", string.Empty }) as string;

            Assert.That(value, Does.StartWith("http://127.0.0.1:54321/"));
        }

        [Test]
        public void McpToolExposure_IsLimitedToFacadeTools()
        {
            var toolMethods = typeof(SarifTools)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Length > 0)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.That(toolMethods, Is.EqualTo(new[] { "manage_triage" }));
        }

        private static object CreateInternalTypeInstance(string typeName)
        {
            var type = typeof(SarifTools).Assembly.GetType(typeName, throwOnError: true)!;
            return Activator.CreateInstance(type)!;
        }

        private static void SetInternalSarifToolsProperty(string propertyName, object? value)
        {
            var property = typeof(SarifTools).GetProperty(propertyName, BindingFlags.Static | BindingFlags.NonPublic);
            property!.SetValue(null, value);
        }
    }
}