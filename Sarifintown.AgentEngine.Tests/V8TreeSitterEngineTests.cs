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
        public async Task ExtractMethodAsync_WithValidCSharpCode_ReturnsAstString()
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
            var language = "c_sharp"; // Note: the wasm file is tree-sitter-c_sharp.wasm

            // Act
            var result = await _engine.ExtractMethodAsync(sourceCode, language, 0, 0);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Contains.Substring("class_declaration"));
            Assert.That(result, Contains.Substring("method_declaration"));
        }

        [Test]
        public async Task ExtractMethodAsync_WithValidJavascriptCode_ReturnsAstString()
        {
            // Arrange
            var sourceCode = @"
            function testMethod() {
                let x = 1;
            }";
            var language = "javascript";

            // Act
            var result = await _engine.ExtractMethodAsync(sourceCode, language, 0, 0);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Contains.Substring("function_declaration"));
        }
    }
}