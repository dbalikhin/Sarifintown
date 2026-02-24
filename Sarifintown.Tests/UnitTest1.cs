using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
// Playwright e2e tests are available on-demand (Category = "Playwright").
using NUnit.Framework;
using FluentAssertions;
using Sarifintown.Helpers;
using Sarifintown.Services;

namespace Sarifintown.Tests
{
    [Parallelizable(ParallelScope.Self)]
    [TestFixture]
    [Category("Playwright")]
    [Explicit("Run Playwright e2e tests on demand")]
    public class Tests : PageTest
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

        [Test]
        public async Task HasTitle()
        {
            await Page.GotoAsync("https://playwright.dev");

            // Expect a title "to contain" a substring.
            await Expect(Page).ToHaveTitleAsync(new Regex("Playwright"));
        }

        [Test]
        public async Task GetStartedLink()
        {
            await Page.GotoAsync("https://playwright.dev");

            // Click the get started link.
            await Page.GetByRole(AriaRole.Link, new() { Name = "Get started" }).ClickAsync();

            // Expects page to have a heading with the name of Installation.
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Installation" })).ToBeVisibleAsync();
        }

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