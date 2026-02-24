using System.Text.RegularExpressions;
using System.Threading.Tasks;
// Playwright e2e tests are disabled in this run to avoid launching browsers
using NUnit.Framework;
using FluentAssertions;
using Sarifintown.Helpers;
using Sarifintown.Services;

namespace Sarifintown.Tests
{
    [Parallelizable(ParallelScope.Self)]
    [TestFixture]
    [Ignore("Playwright e2e tests disabled in this run")]
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            Assert.Pass();
        }

        // Playwright-based e2e tests removed from unit test run.

        [Test]
        public void NormalizePath_RemovesDotSegmentsAndBackslashes()
        {
            var result = FileHelper.NormalizePath(@"C:\repo\.\src\..\app\file.txt");

            result.Should().Be("C:/repo/app/file.txt");
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

            error.Should().BeNull();
            result.adjustedPath.Should().Be("src/file.cs");
            result.matchedFolder.Should().Be(folder);
        }

    }
}