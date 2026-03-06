using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using Sarifintown.Core;
using System.Collections.Concurrent;
using System.Threading;

public class V8TreeSitterEngine : ITreeSitterEngine, IDisposable
{
    private readonly V8ScriptEngine _engine;
    private readonly ConcurrentDictionary<string, byte[]> _languageWasmCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _engineGate = new(1, 1);

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

        // 5. Force WASM compilation for C# grammar by parsing a tiny dummy string
        try
        {
            await ExtractMethodAsync("int x = 1;", "csharp", 1, 1).ConfigureAwait(false);
        }
        catch
        {
            // Ignore errors during warmup
        }
    }

    public async Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return string.Empty;
        }

        var normalizedLanguage = NormalizeLanguage(language);
        var baseDir = AppContext.BaseDirectory;
        var wasmPath = Path.Combine(baseDir, "tree-sitter", $"tree-sitter-{normalizedLanguage}.wasm");

        if (!File.Exists(wasmPath))
        {
            return string.Empty;
        }

        if (!_languageWasmCache.TryGetValue(normalizedLanguage, out var wasmBytes))
        {
            wasmBytes = await File.ReadAllBytesAsync(wasmPath, cancellationToken);
            _languageWasmCache.TryAdd(normalizedLanguage, wasmBytes);
        }

        var targetStartLine = Math.Max(0, startLine);
        var targetEndLine = Math.Max(targetStartLine, endLine);

        await _engineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _engine.Script.sourceCode = sourceCode;
            _engine.Script.wasmBytes = wasmBytes;
            _engine.Script.targetStartLine = targetStartLine;
            _engine.Script.targetEndLine = targetEndLine;

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

                        const methodNodeTypes = new Set([
                            'method_declaration',
                            'function_declaration',
                            'method_definition',
                            'function_definition',
                            'constructor_declaration',
                            'arrow_function',
                            'lambda_expression',
                            'function_item',
                            'function_expression',
                            'function_signature_item',
                            'function',
                            'local_function_statement'
                        ]);

                        const findMethodForLine = (node, targetLine) => {
                            if (!node) {
                                return null;
                            }

                            if (methodNodeTypes.has(node.type)
                                && node.startPosition.row <= targetLine
                                && node.endPosition.row >= targetLine) {
                                return node;
                            }

                            for (let i = 0; i < node.namedChildCount; i++) {
                                const child = node.namedChild(i);
                                if (!child) {
                                    continue;
                                }

                                if (targetLine < child.startPosition.row || targetLine > child.endPosition.row) {
                                    continue;
                                }

                                const found = findMethodForLine(child, targetLine);
                                if (found) {
                                    return found;
                                }
                            }

                            return null;
                        };

                        let node = findMethodForLine(tree.rootNode, targetStartLine);
                        if (!node && targetEndLine !== targetStartLine) {
                            node = findMethodForLine(tree.rootNode, targetEndLine);
                        }

                        while (node && !methodNodeTypes.has(node.type)) {
                            node = node.parent;
                        }

                        if (!node) {
                            return '';
                        }

                        return sourceCode.substring(node.startIndex, node.endIndex);
                    } catch (e) {
                        return 'ERROR: ' + e.toString();
                    }
                })()
            ");

            string result = (string)await ((ScriptObject)promise).ToTask().ConfigureAwait(false);
            return result;
        }
        finally
        {
            _engineGate.Release();
        }
    }

    private static string NormalizeLanguage(string language)
    {
        var normalized = (language ?? string.Empty).Trim().ToLowerInvariant();

        return normalized switch
        {
            "csharp" => "c_sharp",
            "cs" => "c_sharp",
            "typescriptreact" => "tsx",
            "javascriptreact" => "javascript",
            _ => normalized
        };
    }

    public void Dispose()
    {
        _engineGate.Dispose();
        _engine.Dispose();
    }
}