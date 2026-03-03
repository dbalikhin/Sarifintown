using NUnit.Framework;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.Collections.Generic;

namespace Sarifintown.Core.Tests
{
    [TestFixture]
    public class FileHelperTests
    {
        [Test]
        public void NormalizePath_RemovesDotSegmentsAndBackslashes()
        {
            var result = FileHelper.NormalizePath(@"C:\repo\.\src\..\app\file.txt");

            Assert.That(result, Is.EqualTo("C:/repo/app/file.txt"));
        }

        [Test]
        public void AdjustPathToGrantedFolder_DirectMatch_ReturnsAdjustedPath()
        {
            var folder = new DirectoryPicker
            {
                Id = 1,
                Name = "repo",
                Subdirectories = new List<string> { "repo/src", "repo/test" }
            };

            var result = FileHelper.AdjustPathToGrantedFolder("repo/src/file.cs", new[] { folder }, out var error);

            Assert.That(error, Is.Null);
            Assert.That(result.adjustedPath, Is.EqualTo("src/file.cs"));
            Assert.That(result.matchedFolder, Is.EqualTo(folder));
        }

        [Test]
        public void ResolveArtifactPath_WithOriginalUriBaseId_CombinesToAbsoluteFilePath()
        {
            var run = new Run
            {
                OriginalUriBaseIds = new Dictionary<string, UriBaseId>
                {
                    ["SRCROOT"] = new UriBaseId { Uri = "file:///repo/" }
                }
            };

            var artifact = new PhysicalLocation.PhysicalLocationArtifactLocation
            {
                Uri = "src/auth.cs",
                UriBaseId = "SRCROOT"
            };

            var resolved = FileHelper.ResolveArtifactPath(artifact, run);

            Assert.That(resolved, Is.EqualTo("repo/src/auth.cs"));
        }

        [Test]
        public void ResolveArtifactPath_WithArtifactIndex_UsesIndexedLocation()
        {
            var run = new Run
            {
                Artifacts = new List<Artifact>
                {
                    new Artifact
                    {
                        Location = new PhysicalLocation.PhysicalLocationArtifactLocation
                        {
                            Uri = "src/file.cs"
                        }
                    }
                }
            };

            var artifact = new PhysicalLocation.PhysicalLocationArtifactLocation { Index = 0 };

            var resolved = FileHelper.ResolveArtifactPath(artifact, run);

            Assert.That(resolved, Is.EqualTo("src/file.cs"));
        }

        [Test]
        public void RebaseToWorkspaceRelativePath_WithWorkspaceFolder_ReturnsRelativePart()
        {
            var rebased = FileHelper.RebaseToWorkspaceRelativePath("C:/repo/src/a.cs", "repo");

            Assert.That(rebased, Is.EqualTo("src/a.cs"));
        }

        [Test]
        public void AdjustPathToGrantedFolder_ScannerRootRelativePath_PrependsSingleProjectFolder()
        {
            var folder = new DirectoryPicker
            {
                Id = 1,
                Name = "repo",
                Subdirectories = new List<string> { "repo/.sarif", "repo/RepoFolder" }
            };

            var result = FileHelper.AdjustPathToGrantedFolder("Pages/Error.cshtml.cs", new[] { folder }, out var error);

            Assert.That(error, Is.Null);
            Assert.That(result.adjustedPath, Is.EqualTo("RepoFolder/Pages/Error.cshtml.cs"));
            Assert.That(result.matchedFolder, Is.EqualTo(folder));
        }
    }
}
