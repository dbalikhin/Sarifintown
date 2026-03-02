using NUnit.Framework;
using Sarifintown.Helpers;
using Sarifintown.Models;

namespace Sarifintown.Core.Tests
{
    [TestFixture]
    public class SarifTriageIdentityHelperTests
    {
        [Test]
        public void BuildIdentity_WithPartialFingerprints_ReturnsDeterministicIdentity()
        {
            var result = new Result
            {
                PartialFingerprints = new Dictionary<string, string>
                {
                    ["primaryLocationLineHash"] = "abc",
                    ["primaryLocationStartColumnFingerprint"] = "42"
                }
            };

            var first = SarifTriageIdentityHelper.BuildIdentity(result);
            var second = SarifTriageIdentityHelper.BuildIdentity(result);

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void BuildIdentity_WithoutFingerprints_UsesFallbackData()
        {
            var result = new Result
            {
                RuleId = "RULE001",
                Message = new Result.ResultMessage { Text = "message" },
                Locations = new List<ResultLocation>
                {
                    new ResultLocation
                    {
                        PhysicalLocation = new PhysicalLocation
                        {
                            ArtifactLocation = new PhysicalLocation.PhysicalLocationArtifactLocation
                            {
                                Uri = "src/file.cs"
                            },
                            Region = new Region
                            {
                                StartLine = 1,
                                StartColumn = 1,
                                EndLine = 1,
                                EndColumn = 10
                            }
                        }
                    }
                }
            };

            var identity = SarifTriageIdentityHelper.BuildIdentity(result);

            Assert.That(identity, Is.Not.Empty);
            Assert.That(identity.Length, Is.EqualTo(64));
        }
    }
}
