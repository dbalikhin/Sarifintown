using Sarifintown.Models;
using Sarifintown.Services;

namespace Sarifintown.Tests
{
    [TestFixture]
    public class SarifFileServiceTests
    {
        [Test]
        public void AddSarifFile_AssignsIdentityAndOriginalPath()
        {
            var service = new SarifFileService();
            var sarifLog = CreateSarifLog(level: "warning", ruleId: "RULE001", path: "src/auth.cs");
            var sarifFile = new SarifFile("test.sarif", 123, sarifLog);

            var added = service.AddSarifFile(sarifFile, jsDirectoryId: 7);

            Assert.That(added, Is.True);
            var result = sarifLog.Runs[0].Results[0];
            Assert.That(result.ResultIdentity, Is.Not.Null.And.Not.Empty);
            Assert.That(result.OriginalFilenamePath, Is.EqualTo("src/auth.cs"));
            Assert.That(sarifLog.Runs[0].JSDirectoryId, Is.EqualTo(7));
        }

        [Test]
        public void GetFilteredResultIdentities_WithSeverityAndRule_ReturnsMatchingResultOnly()
        {
            var service = new SarifFileService();
            var sarifLog = new SarifLog
            {
                Version = "2.1.0",
                Runs =
                [
                    new Run
                    {
                        Tool = new Tool
                        {
                            Driver = new Tool.ToolDriver
                            {
                                Rule =
                                [
                                    new Rule { Id = "RULE001", DefaultConfiguration = new Rule.RuleDefaultConfiguration { Level = "warning" } },
                                    new Rule { Id = "RULE002", DefaultConfiguration = new Rule.RuleDefaultConfiguration { Level = "error" } }
                                ]
                            }
                        },
                        Results =
                        [
                            CreateResult("RULE001", "warning", "src/a.cs"),
                            CreateResult("RULE002", "error", "src/b.cs")
                        ]
                    }
                ]
            };

            service.AddSarifFile(new SarifFile("multi.sarif", 100, sarifLog));
            var run = sarifLog.Runs[0];
            var selectedRules = new[] { new RuleWithCount { Rule = new Rule { Id = "RULE002" }, Count = 1 } };

            var filtered = service.GetFilteredResultIdentities(run, new[] { "error" }, selectedRules);

            Assert.That(filtered.Count, Is.EqualTo(1));
            Assert.That(filtered.Contains(run.Results[1].ResultIdentity), Is.True);
        }

        [Test]
        public void FindResultByIdentity_WithKnownIdentity_ReturnsRunAndResult()
        {
            var service = new SarifFileService();
            var sarifLog = CreateSarifLog(level: "note", ruleId: "RULE001", path: "src/file.cs");
            service.AddSarifFile(new SarifFile("one.sarif", 64, sarifLog));

            var identity = sarifLog.Runs[0].Results[0].ResultIdentity;
            var found = service.FindResultByIdentity(identity);

            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Value.Result.RuleId, Is.EqualTo("RULE001"));
        }

        private static SarifLog CreateSarifLog(string level, string ruleId, string path)
        {
            return new SarifLog
            {
                Version = "2.1.0",
                Runs =
                [
                    new Run
                    {
                        Tool = new Tool
                        {
                            Driver = new Tool.ToolDriver
                            {
                                Rule =
                                [
                                    new Rule
                                    {
                                        Id = ruleId,
                                        DefaultConfiguration = new Rule.RuleDefaultConfiguration { Level = level }
                                    }
                                ]
                            }
                        },
                        Results =
                        [
                            CreateResult(ruleId, level, path)
                        ]
                    }
                ]
            };
        }

        private static Result CreateResult(string ruleId, string level, string path)
        {
            return new Result
            {
                RuleId = ruleId,
                RuleIndex = 0,
                Level = level,
                Message = new Result.ResultMessage { Text = "issue" },
                Locations =
                [
                    new ResultLocation
                    {
                        PhysicalLocation = new PhysicalLocation
                        {
                            ArtifactLocation = new PhysicalLocation.PhysicalLocationArtifactLocation { Uri = path },
                            Region = new Region { StartLine = 10, StartColumn = 1, EndLine = 10, EndColumn = 12 }
                        }
                    }
                ]
            };
        }
    }
}
