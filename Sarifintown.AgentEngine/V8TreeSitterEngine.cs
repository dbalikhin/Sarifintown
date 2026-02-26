using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
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
        var baseDir = AppContext.BaseDirectory;
        var treeSitterJsPath = Path.Combine(baseDir, "tree-sitter", "tree-sitter.js");
        var treeSitterWasmPath = Path.Combine(baseDir, "tree-sitter", "tree-sitter.wasm"); // Core WASM engine

        // 1. Read JS and Core WASM
        var treeSitterJs = await File.ReadAllTextAsync(treeSitterJsPath);
        var coreWasmBytes = await File.ReadAllBytesAsync(treeSitterWasmPath);

        _engine.Execute(treeSitterJs);
        _engine.Execute("var console = { log: function(msg) {}, error: function(msg) {} };");

        // 2. Pass core WASM bytes to JS
        _engine.Script.coreWasmBytes = coreWasmBytes;

        // 3. Setup global initialization script with the wasmBinary injected
        _engine.Execute(@"
            let parser = null;
            let currentLanguageName = null;
            
            async function initTreeSitter() {
                // Convert .NET byte array to JS Uint8Array
                const coreArray = new Uint8Array(coreWasmBytes.Length);
                for (let i = 0; i < coreWasmBytes.Length; i++) {
                    coreArray[i] = coreWasmBytes[i];
                }

                // Pass the WASM binary directly to TreeSitter's Emscripten bootstrapper
                await TreeSitter.init({
                    wasmBinary: coreArray
                });
                
                parser = new TreeSitter();
            }
        ");

        // 4. Properly AWAIT the JS Promise so initialization completes before parsing
        var initPromise = _engine.Evaluate("initTreeSitter()");
        await ((ScriptObject)initPromise).ToTask();
    }

    public async Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine)
    {
        var baseDir = AppContext.BaseDirectory;
        var wasmPath = Path.Combine(baseDir, "tree-sitter", $"tree-sitter-{language.ToLower()}.wasm");

        var wasmBytes = await File.ReadAllBytesAsync(wasmPath);

        _engine.Script.sourceCode = sourceCode;
        _engine.Script.wasmBytes = wasmBytes;

        var promise = _engine.Evaluate(@"
            (async () => {
                try {
                    const wasmArray = new Uint8Array(wasmBytes.Length);
                    for (let i = 0; i < wasmBytes.Length; i++) {
                        wasmArray[i] = wasmBytes[i];
                    }
                    
                    const lang = await TreeSitter.Language.load(wasmArray);
                    parser.setLanguage(lang);

                    const tree = parser.parse(sourceCode);

                    return tree.rootNode.toString();
                } catch (e) {
                    return 'ERROR: ' + e.toString();
                }
            })()
        ");

        // Properly cast and await the result
        string result = (string)await ((ScriptObject)promise).ToTask();
        return result;
    }

    public void Dispose() => _engine.Dispose();
}