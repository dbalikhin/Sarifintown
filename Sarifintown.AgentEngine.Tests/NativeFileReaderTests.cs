using NUnit.Framework;
using Sarifintown.AgentEngine;

namespace Sarifintown.AgentEngine.Tests
{
    [TestFixture]
    public class NativeFileReaderTests
    {
        private string _tempDirectory;

        [SetUp]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Test]
        public async Task ReadFileAsync_WithValidFile_ReturnsContent()
        {
            // Arrange
            var fileName = "test.txt";
            var expectedContent = "Hello, World!";
            var fullPath = Path.Combine(_tempDirectory, fileName);
            await File.WriteAllTextAsync(fullPath, expectedContent);

            var reader = new NativeFileReader(_tempDirectory);

            // Act
            var actualContent = await reader.ReadFileAsync(fileName);

            // Assert
            Assert.That(actualContent, Is.EqualTo(expectedContent));
        }

        [Test]
        public void ReadFileAsync_WithNonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var reader = new NativeFileReader(_tempDirectory);

            // Act & Assert
            Assert.ThrowsAsync<FileNotFoundException>(() => reader.ReadFileAsync("nonexistent.txt"));
        }
    }
}