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
    }
}