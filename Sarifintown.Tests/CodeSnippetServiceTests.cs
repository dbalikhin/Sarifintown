using Sarifintown.Core;
using Sarifintown.Models;
using Sarifintown.Services;

namespace Sarifintown.Tests
{
    [TestFixture]
    public class CodeSnippetServiceTests
    {
        [Test]
        public async Task EnsureCodeSnippetAsync_WhenCalledTwice_UsesCacheForSecondCall()
        {
            var fileReader = new CountingFileReader();
            fileReader.Files["src/file.cs"] = "line1\nline2\nline3\nline4";

            var localFilesService = new LocalFilesService();
            localFilesService.AddDirectory(new DirectoryPicker { Id = 1, Name = "src" });
            var settingsService = new SettingsService();

            var service = new CodeSnippetService(fileReader, localFilesService, settingsService);
            var run = new Run { Results = new List<Result>() };
            var result = CreateResult();
            run.Results.Add(result);

            var first = await service.EnsureCodeSnippetAsync(run, result);
            var second = await service.EnsureCodeSnippetAsync(run, result);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(fileReader.ReadCount, Is.EqualTo(1));
            Assert.That(result.IsSnippetLoaded, Is.True);
            Assert.That(result.Locations[0].PhysicalLocation.ExtractedCodeSnippet, Is.Not.Null);
        }

        [Test]
        public async Task AddCodeSnippetsToRunAsync_LoadsSnippetsInBatch()
        {
            var fileReader = new CountingFileReader();
            fileReader.Files["src/file1.cs"] = "a\nb\nc\nd\ne";
            fileReader.Files["src/file2.cs"] = "f\ng\nh\ni\nj";

            var localFilesService = new LocalFilesService();
            localFilesService.AddDirectory(new DirectoryPicker { Id = 2, Name = "src" });
            var settingsService = new SettingsService();

            var service = new CodeSnippetService(fileReader, localFilesService, settingsService);
            var run = new Run
            {
                Results =
                [
                    CreateResult("src/file1.cs"),
                    CreateResult("src/file2.cs")
                ]
            };

            var callbackCount = 0;
            var response = await service.AddCodeSnippetsToRunAsync(
                run,
                onSnippetAdded: () => callbackCount++,
                batchSize: 1);

            Assert.That(response.Success, Is.True);
            Assert.That(run.Results.All(result => result.IsSnippetLoaded), Is.True);
            Assert.That(callbackCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task EnsureCodeSnippetAsync_WhenSurroundingLinesChanged_UsesConfiguredWindow()
        {
            var fileReader = new CountingFileReader();
            fileReader.Files["src/file.cs"] = "line1\nline2\nline3\nline4\nline5\nline6";

            var localFilesService = new LocalFilesService();
            localFilesService.AddDirectory(new DirectoryPicker { Id = 3, Name = "src" });

            var settingsService = new SettingsService { SurroundingLines = 1 };
            var service = new CodeSnippetService(fileReader, localFilesService, settingsService);

            var run = new Run { Results = new List<Result>() };
            var result = CreateResult("src/file.cs");
            run.Results.Add(result);

            var response = await service.EnsureCodeSnippetAsync(run, result);

            Assert.That(response.Success, Is.True);
            Assert.That(result.Locations[0].PhysicalLocation.ExtractedCodeSnippet!.VisibleStartLine, Is.EqualTo(1));
            Assert.That(result.Locations[0].PhysicalLocation.ExtractedCodeSnippet!.VisibleEndLine, Is.EqualTo(3));
        }

        private static Result CreateResult(string path = "src/file.cs")
        {
            return new Result
            {
                RuleId = "RULE001",
                Level = "warning",
                Message = new Result.ResultMessage { Text = "issue" },
                Locations =
                [
                    new ResultLocation
                    {
                        PhysicalLocation = new PhysicalLocation
                        {
                            ArtifactLocation = new PhysicalLocation.PhysicalLocationArtifactLocation { Uri = path },
                            Region = new Region { StartLine = 2, StartColumn = 1, EndLine = 2, EndColumn = 4 }
                        }
                    }
                ]
            };
        }

        private sealed class CountingFileReader : IFileReader
        {
            public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

            public int ReadCount { get; private set; }

            public Task<string> ReadFileAsync(string relativePath)
            {
                ReadCount++;
                if (Files.TryGetValue(relativePath, out var content))
                {
                    return Task.FromResult(content);
                }

                return Task.FromResult(string.Empty);
            }
        }
    }
}
