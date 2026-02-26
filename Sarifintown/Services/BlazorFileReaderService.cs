using Microsoft.JSInterop;
using Sarifintown.Core;
using Sarifintown.Models;

namespace Sarifintown.Services
{
    public class BlazorFileReaderService : IFileReader
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly LocalFilesService _localFilesService;

        public BlazorFileReaderService(IJSRuntime jsRuntime, LocalFilesService localFilesService)
        {
            _jsRuntime = jsRuntime;
            _localFilesService = localFilesService;
        }

        public async Task<string> ReadFileAsync(string relativePath)
        {
            foreach (var directory in _localFilesService.AllDirectories)
            {
                try
                {
                    string content = await _jsRuntime.InvokeAsync<string>(
                        "fileSystemHelpers.readFileContent",
                        directory.Id, // Extract the ID from your picker model
                        relativePath);

                    if (!string.IsNullOrEmpty(content))
                    {
                        return content;
                    }
                }
                catch
                {
                    continue;
                }
            }

            Console.Error.WriteLine($"File '{relativePath}' not found in any registered directories.");
            return string.Empty;
        }
    }
}
