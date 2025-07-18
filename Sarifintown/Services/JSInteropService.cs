using Microsoft.JSInterop;

namespace Sarifintown.Services
{
    public class JSInteropService
    {
        private readonly IJSRuntime _jsRuntime;

        public JSInteropService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<string> ReadFileContentAsync(int directoryId, string fileName)
        {
            try
            {
                string content = await _jsRuntime.InvokeAsync<string>(
                    "fileSystemHelpers.readFileContent",
                    directoryId,
                    fileName);

                return content;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An error occurred: {ex.Message}");
                return string.Empty;
            }
        }

    }

}
