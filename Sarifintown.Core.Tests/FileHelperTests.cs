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
    }
}
