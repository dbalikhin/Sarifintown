using NUnit.Framework;
using Sarifintown.AgentEngine;
using Sarifintown.Core;
using System.Text.Json;

namespace Sarifintown.AgentEngine.Tests
{
    [TestFixture]
    public class SarifToolsTests
    {
        private class FakeFileReader : IFileReader
        {
            public Dictionary<string, string> Files { get; } = new();

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

        private class FakeTreeSitterEngine : ITreeSitterEngine
        {
            public Task InitializeAsync() => Task.CompletedTask;

            public Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine)
            {
                return Task.FromResult("extracted_code_snippet");
            }
        }

        [SetUp]
        public void Setup()
        {
            SarifTools.FileReader = new FakeFileReader();
            SarifTools.TreeSitterEngine = new FakeTreeSitterEngine();
            SarifTools.SetDiscoveredSarifFiles(Array.Empty<string>());
            SarifTools.SetLocalUiBaseUrl(string.Empty);
            SarifTools.SetWorkspaceRoot(Directory.GetCurrentDirectory());
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
        public async Task LoadAndFilterSarif_WithDiscoveredFileName_ResolvesAndParsesFile()
        {
            // Arrange
            var sarifContent = @"
            {
                ""runs"": [
                    {
                        ""results"": [
                            {
                                ""ruleId"": ""RULE-DISCOVERED"",
                                ""level"": ""warning"",
                                ""message"": { ""text"": ""From discovered file"" }
                            }
                        ]
                    }
                ]
            }";

            var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);
            var tempFile = Path.Combine(tempDirectory, "scan.sarif");
            File.WriteAllText(tempFile, sarifContent);
            SarifTools.SetDiscoveredSarifFiles(new[] { tempFile });

            try
            {
                // Act
                var result = await SarifTools.LoadAndFilterSarif("scan.sarif");

                // Assert
                Assert.That(result, Contains.Substring("RULE-DISCOVERED"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ListWorkspaceSarifFiles_WithDiscoveredFiles_ReturnsSerializedList()
        {
            // Arrange
            var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);
            var fileOne = Path.Combine(tempDirectory, "a.sarif");
            var fileTwo = Path.Combine(tempDirectory, "b.sarif");
            File.WriteAllText(fileOne, "{}");
            File.WriteAllText(fileTwo, "{}");
            SarifTools.SetDiscoveredSarifFiles(new[] { fileOne, fileTwo });

            try
            {
                // Act
                var json = SarifTools.ListWorkspaceSarifFiles();
                var parsed = JsonSerializer.Deserialize<List<JsonElement>>(json);

                // Assert
                Assert.That(parsed, Is.Not.Null);
                Assert.That(parsed!.Count, Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public async Task LoadAndFilterSarif_WhenFileDoesNotExist_ReturnsError()
        {
            // Arrange
            var path = "nonexistent.sarif";

            // Act
            var result = await SarifTools.LoadAndFilterSarif(path);

            // Assert
            Assert.That(result, Contains.Substring("File not found"));
        }

        [Test]
        public async Task LoadAndFilterSarif_WithValidSarif_ReturnsFilteredResults()
        {
            // Arrange
            var sarifContent = @"
            {
                ""runs"": [
                    {
                        ""results"": [
                            {
                                ""ruleId"": ""RULE001"",
                                ""level"": ""error"",
                                ""message"": { ""text"": ""Test error"" }
                            },
                            {
                                ""ruleId"": ""RULE002"",
                                ""level"": ""warning"",
                                ""message"": { ""text"": ""Test warning"" }
                            }
                        ]
                    }
                ]
            }";

            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, sarifContent);

            try
            {
                // Act
                var result = await SarifTools.LoadAndFilterSarif(tempFile, severity: "error");

                // Assert
                Assert.That(result, Contains.Substring("RULE001"));
                Assert.That(result, Does.Not.Contain("RULE002"));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public async Task ExtractCodeFlow_WithValidData_ReturnsFlowSteps()
        {
            // Arrange
            var sarifContent = @"
            {
                ""runs"": [
                    {
                        ""results"": [
                            {
                                ""ruleId"": ""RULE001"",
                                ""codeFlows"": [
                                    {
                                        ""threadFlows"": [
                                            {
                                                ""locations"": [
                                                    {
                                                        ""location"": {
                                                            ""physicalLocation"": {
                                                                ""artifactLocation"": {
                                                                    ""uri"": ""test.cs""
                                                                },
                                                                ""region"": {
                                                                    ""startLine"": 10
                                                                }
                                                            },
                                                            ""message"": {
                                                                ""text"": ""Step 1""
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
            }";

            var tempSarifFile = Path.GetTempFileName();
            File.WriteAllText(tempSarifFile, sarifContent);

            var tempSourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempSourceDir);
            var tempSourceFile = Path.Combine(tempSourceDir, "test.cs");
            File.WriteAllText(tempSourceFile, "var x = 1;\nvar y = 2;\nvar z = 3;");

            try
            {
                // Act
                var result = await SarifTools.ExtractCodeFlow(tempSarifFile, "0", tempSourceDir);

                // Assert
                Assert.That(result, Contains.Substring("RULE001"));
                Assert.That(result, Contains.Substring("test.cs"));
                Assert.That(result, Contains.Substring("extracted_code_snippet"));
            }
            finally
            {
                File.Delete(tempSarifFile);
                Directory.Delete(tempSourceDir, true);
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
    }
}