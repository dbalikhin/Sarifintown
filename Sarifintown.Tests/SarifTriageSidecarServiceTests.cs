using Microsoft.JSInterop;
using Sarifintown.Helpers;
using Sarifintown.Models;
using Sarifintown.Services;
using System.Text.Json;

namespace Sarifintown.Tests
{
    [TestFixture]
    public class SarifTriageSidecarServiceTests
    {
        [Test]
        public async Task ApplySuppressions_WithMatchingIdentity_AddsSuppressionToResult()
        {
            var result = new Result
            {
                RuleId = "RULE001",
                Message = new Result.ResultMessage { Text = "Issue" },
                Locations = new List<ResultLocation>
                {
                    new ResultLocation
                    {
                        PhysicalLocation = new PhysicalLocation
                        {
                            ArtifactLocation = new PhysicalLocation.PhysicalLocationArtifactLocation { Uri = "src/a.cs" },
                            Region = new Region { StartLine = 10, StartColumn = 1, EndLine = 10, EndColumn = 20 }
                        }
                    }
                }
            };

            var identity = SarifTriageIdentityHelper.BuildIdentity(result);
            var sidecar = new SarifTriageSidecar
            {
                Suppressions =
                [
                    new SarifTriageSuppressionEntry
                    {
                        Identity = identity,
                        RuleId = "RULE001",
                        Path = "src/a.cs",
                        StartLine = 10,
                        Suppression = new Suppression
                        {
                            Kind = "external",
                            Status = "accepted",
                            Justification = "Triaged"
                        }
                    }
                ]
            };

            var sidecarJson = JsonSerializer.Serialize(sidecar);
            var jsRuntime = new FakeJsRuntime(sidecarJson);
            var service = new SarifTriageSidecarService(jsRuntime);

            await service.PrimeDirectoryAsync(1);

            var sarifLog = new SarifLog
            {
                Version = "2.1.0",
                Runs =
                [
                    new Run
                    {
                        Results = [result]
                    }
                ]
            };

            service.ApplySuppressions(sarifLog, 1);

            Assert.That(result.Suppressions, Has.Count.EqualTo(1));
            Assert.That(result.Suppressions[0].Status, Is.EqualTo("accepted"));
        }

        private sealed class FakeJsRuntime : IJSRuntime
        {
            private readonly string _sidecarJson;

            public FakeJsRuntime(string sidecarJson)
            {
                _sidecarJson = sidecarJson;
            }

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            {
                return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
            }

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            {
                if (identifier == "fileSystemHelpers.readTextFile")
                {
                    return ValueTask.FromResult((TValue)(object)_sidecarJson);
                }

                if (identifier == "scriptLoader.ensure")
                {
                    return ValueTask.FromResult(default(TValue)!);
                }

                throw new InvalidOperationException($"Unexpected JS invocation: {identifier}");
            }
        }
    }
}
