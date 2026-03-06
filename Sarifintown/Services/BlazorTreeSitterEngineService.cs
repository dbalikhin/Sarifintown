using Microsoft.JSInterop;
using Sarifintown.Core;

namespace Sarifintown.Services
{
    public class BlazorTreeSitterEngineService : ITreeSitterEngine
    {
        private readonly IJSRuntime _jsRuntime;

        public BlazorTreeSitterEngineService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync()
        {
            await _jsRuntime.InvokeVoidAsync("TreeSitterInterop.initializeWorker");
        }

        public async Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine, CancellationToken cancellationToken = default)
        {
            // Wraps your existing JSInterop call to tree-sitter-interop.js
            // Note: extractMethodBySnippetPosition takes line, startColumn, endColumn, needAdjustment
            // We pass startLine as line, and 0 for columns as a placeholder
            var result = await _jsRuntime.InvokeAsync<object>("TreeSitterInterop.extractMethodBySnippetPosition", cancellationToken, sourceCode, language, startLine, 0, 0, false);
            return result?.ToString() ?? string.Empty;
        }
    }
}
