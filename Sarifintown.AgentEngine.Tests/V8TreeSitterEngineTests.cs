using NUnit.Framework;
using Sarifintown.AgentEngine;

namespace Sarifintown.AgentEngine.Tests
{
    [TestFixture]
    public class V8TreeSitterEngineTests
    {
        private V8TreeSitterEngine _engine;

        [SetUp]
        public async Task Setup()
        {
            _engine = new V8TreeSitterEngine();
            await _engine.InitializeAsync();
        }

        [TearDown]
        public void TearDown()
        {
            _engine?.Dispose();
        }

        [Test]
        public async Task ExtractMethodAsync_WithValidCSharpCode_ReturnsMethodBody()
        {
            // Arrange
            var sourceCode = @"
            public class TestClass
            {
                public void TestMethod()
                {
                    int x = 1;
                }
            }";
            var language = "csharp";

            // Act
            var result = await _engine.ExtractMethodAsync(sourceCode, language, 2, 4);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Contains.Substring("public void TestMethod()"));
            Assert.That(result, Contains.Substring("int x = 1;"));
        }

        [Test]
        public async Task ExtractMethodAsync_WithValidJavascriptCode_ReturnsFunctionBody()
        {
            // Arrange
            var sourceCode = @"
            function testMethod() {
                let x = 1;
            }";
            var language = "javascript";

            // Act
            var result = await _engine.ExtractMethodAsync(sourceCode, language, 1, 1);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Contains.Substring("function testMethod()"));
        }

        [Test]
        public async Task ExtractMethodAsync_WithUnknownLanguage_ReturnsEmptyForFallback()
        {
            var sourceCode = "line1\nline2";

            var result = await _engine.ExtractMethodAsync(sourceCode, "unknownlang", 0, 0);

            Assert.That(result, Is.EqualTo(string.Empty));
        }
    }
}