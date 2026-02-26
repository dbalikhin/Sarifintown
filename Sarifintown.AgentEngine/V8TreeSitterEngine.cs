using Microsoft.ClearScript.V8;
using Sarifintown.Core;

public class V8TreeSitterEngine : ITreeSitterEngine, IDisposable
{
    private readonly V8ScriptEngine _engine;

    public V8TreeSitterEngine()
    {
        _engine = new V8ScriptEngine();
    }

    public async Task InitializeAsync()
    {
        // 1. Load the web-tree-sitter JS glue code you already have
        var treeSitterJs = await File.ReadAllTextAsync("wwwroot/js/tree-sitter/tree-sitter.js");
        _engine.Execute(treeSitterJs);

        // 2. Provide a mock for console.log if tree-sitter uses it
        _engine.Execute("var console = { log: function(msg) {}, error: function(msg) {} };");

        // 3. Setup global initialization script
        _engine.Execute(@"
            let parser = null;
            let currentLanguageName = null;
            
            async function initTreeSitter() {
                await TreeSitter.init();
                parser = new TreeSitter();
            }
            // Trigger init synchronously for setup (or wrap in promise block)
        ");

        await Task.Run(() => _engine.Execute("initTreeSitter()"));
    }

    public async Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine)
    {
        // 1. Read the language WASM file bytes from disk natively in C#
        var wasmPath = $"wwwroot/js/tree-sitter/tree-sitter-{language.ToLower()}.wasm";
        var wasmBytes = await File.ReadAllBytesAsync(wasmPath);

        // 2. Push bytes to JS memory and execute your existing extraction logic
        _engine.Script.sourceCode = sourceCode;
        _engine.Script.wasmBytes = wasmBytes;

        // Notice the 'dynamic' keyword here
        dynamic promise = _engine.Evaluate(@"
            (async () => {
                const wasmArray = new Uint8Array(wasmBytes.ToArray());
                const lang = await TreeSitter.Language.load(wasmArray);
                parser.setLanguage(lang);
                
                const tree = parser.parse(sourceCode);
                
                // Return the string natively back to C#
                return tree.rootNode.toString();
            })()
        ");

        string result = await promise;
        return result;
    }

    public void Dispose() => _engine.Dispose();
}