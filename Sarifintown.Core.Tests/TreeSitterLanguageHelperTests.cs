using NUnit.Framework;
using Sarifintown.Helpers;

namespace Sarifintown.Core.Tests
{
    [TestFixture]
    public class TreeSitterLanguageHelperTests
    {
        [TestCase("c", "c")]
        [TestCase("cs", "csharp")]
        [TestCase("cpp", "cpp")]
        [TestCase("go", "go")]
        [TestCase("java", "java")]
        [TestCase("js", "javascript")]
        [TestCase("kt", "kotlin")]
        [TestCase("php", "php")]
        [TestCase("py", "python")]
        [TestCase("rb", "ruby")]
        [TestCase("rs", "rust")]
        [TestCase("ts", "typescript")]
        [TestCase("unknown", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void GetLanguageByExtension_ReturnsExpectedLanguage(string? extension, string expectedLanguage)
        {
            var result = TreeSitterLanguageHelper.GetLanguageByExtension(extension!);
            Assert.That(result, Is.EqualTo(expectedLanguage));
        }
    }
}
