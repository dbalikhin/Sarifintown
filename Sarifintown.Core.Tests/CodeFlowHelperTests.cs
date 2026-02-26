using NUnit.Framework;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.Collections.Generic;

namespace Sarifintown.Core.Tests
{
    [TestFixture]
    public class CodeFlowHelperTests
    {
        [Test]
        public void PrepareCodeResults_WithZeroDirectoryId_ReturnsEmptyList()
        {
            var result = new Result();
            var directories = new List<DirectoryPicker>();

            var codeFlows = CodeFlowHelper.PrepareCodeResults(result, 0, directories);

            Assert.That(codeFlows, Is.Empty);
        }

        [Test]
        public void PrepareCodeResults_WithValidData_ReturnsCodeFlowData()
        {
            var result = new Result
            {
                CodeFlows = new List<CodeFlow>
                {
                    new CodeFlow
                    {
                        ThreadFlows = new List<ThreadFlow>
                        {
                            new ThreadFlow
                            {
                                Locations = new List<ThreadFlowLocation>
                                {
                                    new ThreadFlowLocation
                                    {
                                        Location = new ResultLocation
                                        {
                                            Id = 1,
                                            PhysicalLocation = new PhysicalLocation
                                            {
                                                ArtifactLocation = new PhysicalLocation.PhysicalLocationArtifactLocation { Uri = "src/file.cs" },
                                                Region = new Region { StartLine = 10, EndLine = 12, StartColumn = 5, EndColumn = 20 }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var directories = new List<DirectoryPicker>
            {
                new DirectoryPicker { Name = "src", Subdirectories = new List<string>() }
            };

            var codeFlows = CodeFlowHelper.PrepareCodeResults(result, 1, directories);

            Assert.That(codeFlows, Has.Count.EqualTo(1));
            Assert.That(codeFlows[0].Id, Is.EqualTo(1));
            Assert.That(codeFlows[0].Filename, Is.EqualTo("src/file.cs"));
            Assert.That(codeFlows[0].FilenameExt, Is.EqualTo("cs"));
            Assert.That(codeFlows[0].Region.StartLine, Is.EqualTo(10));
            Assert.That(codeFlows[0].Region.EndLine, Is.EqualTo(12));
            Assert.That(codeFlows[0].Region.StartColumn, Is.EqualTo(5));
            Assert.That(codeFlows[0].Region.EndColumn, Is.EqualTo(20));
        }

        [Test]
        public void PrepareCodeResults_WithOverlappingRegions_ExpandsColumns()
        {
            var result = new Result
            {
                CodeFlows = new List<CodeFlow>
                {
                    new CodeFlow
                    {
                        ThreadFlows = new List<ThreadFlow>
                        {
                            new ThreadFlow
                            {
                                Locations = new List<ThreadFlowLocation>
                                {
                                    new ThreadFlowLocation
                                    {
                                        Location = new ResultLocation
                                        {
                                            Id = 1,
                                            PhysicalLocation = new PhysicalLocation
                                            {
                                                ArtifactLocation = new PhysicalLocation.PhysicalLocationArtifactLocation { Uri = "src/file.cs" },
                                                Region = new Region { StartLine = 10, EndLine = 12, StartColumn = 5, EndColumn = 20 }
                                            }
                                        }
                                    },
                                    new ThreadFlowLocation
                                    {
                                        Location = new ResultLocation
                                        {
                                            Id = 2,
                                            PhysicalLocation = new PhysicalLocation
                                            {
                                                ArtifactLocation = new PhysicalLocation.PhysicalLocationArtifactLocation { Uri = "src/file.cs" },
                                                Region = new Region { StartLine = 10, EndLine = 12, StartColumn = 2, EndColumn = 25 }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var directories = new List<DirectoryPicker>
            {
                new DirectoryPicker { Name = "src", Subdirectories = new List<string>() }
            };

            var codeFlows = CodeFlowHelper.PrepareCodeResults(result, 1, directories);

            Assert.That(codeFlows, Has.Count.EqualTo(1));
            Assert.That(codeFlows[0].Region.StartColumn, Is.EqualTo(2));
            Assert.That(codeFlows[0].Region.EndColumn, Is.EqualTo(25));
        }
    }
}
