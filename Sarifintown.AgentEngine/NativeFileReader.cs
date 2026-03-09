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
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

    }
}
