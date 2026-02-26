using Sarifintown.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sarifintown.AgentEngine
{
    public class NativeFileReader : IFileReader
    {
        private readonly string _baseDirectory;
        public NativeFileReader(string baseDirectory) => _baseDirectory = baseDirectory;

        public async Task<string> ReadFileAsync(string relativePath)
        {
            var fullPath = Path.Combine(_baseDirectory, relativePath);
            return await File.ReadAllTextAsync(fullPath);
        }

        public Task<string> ReadFileContentAsync(int directoryId, string fileName)
        {
            throw new NotImplementedException();
        }
    }
}
