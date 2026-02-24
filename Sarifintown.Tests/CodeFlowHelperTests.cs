using FluentAssertions;
using NUnit.Framework;
using Sarifintown.Helpers;
using Sarifintown.Models;
using Sarifintown.Services;
using System.Collections.Generic;

namespace Sarifintown.Tests
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

            codeFlows.Should().BeEmpty();
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

            codeFlows.Should().HaveCount(1);
            codeFlows[0].Id.Should().Be(1);
            codeFlows[0].Filename.Should().Be("src/file.cs");
            codeFlows[0].FilenameExt.Should().Be("cs");
            codeFlows[0].Region.StartLine.Should().Be(10);
            codeFlows[0].Region.EndLine.Should().Be(12);
            codeFlows[0].Region.StartColumn.Should().Be(5);
            codeFlows[0].Region.EndColumn.Should().Be(20);
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

            codeFlows.Should().HaveCount(1);
            codeFlows[0].Region.StartColumn.Should().Be(2);
            codeFlows[0].Region.EndColumn.Should().Be(25);
        }
    }
}