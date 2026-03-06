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
    private readonly HashSet<string> _jsLoadedLanguages = new(StringComparer.OrdinalIgnoreCase);

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

        // 2. Transfer core WASM bytes efficiently via ArrayBuffer bulk copy
        var coreBuffer = (IArrayBuffer)_engine.Evaluate($"new ArrayBuffer({coreWasmBytes.Length})");
        coreBuffer.WriteBytes(coreWasmBytes, 0, (ulong)coreWasmBytes.Length, 0);
        _engine.Script.coreWasmBuffer = coreBuffer;

        // 3. Setup global initialization with JS-side language cache to avoid
        //    repeated WASM compilation per ExtractMethodAsync call
        _engine.Execute(@"
            let parser = null;
            let currentLanguageName = null;
            let loadedLanguages = {};

            async function initTreeSitter() {
                const coreArray = new Uint8Array(coreWasmBuffer);
                await TreeSitter.init({
                    wasmBinary: coreArray
                });
                parser = new TreeSitter();
            }

            async function loadLanguageIfNeeded(langName) {
                if (currentLanguageName === langName) {
                    return;
                }
                if (!loadedLanguages[langName]) {
                    const wasmArray = new Uint8Array(pendingWasmBuffer);
                    loadedLanguages[langName] = await TreeSitter.Language.load(wasmArray);
                }
                parser.setLanguage(loadedLanguages[langName]);
                currentLanguageName = langName;
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

        if (!_languageWasmCache.ContainsKey(normalizedLanguage))
        {
            var bytes = await File.ReadAllBytesAsync(wasmPath, cancellationToken);
            _languageWasmCache.TryAdd(normalizedLanguage, bytes);
        }

        var targetStartLine = Math.Max(0, startLine);
        var targetEndLine = Math.Max(targetStartLine, endLine);

        await _engineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Transfer WASM bytes to V8 only for languages not yet loaded in JS
            if (!_jsLoadedLanguages.Contains(normalizedLanguage)
                && _languageWasmCache.TryGetValue(normalizedLanguage, out var wasmBytes))
            {
                var arrayBuffer = (IArrayBuffer)_engine.Evaluate($"new ArrayBuffer({wasmBytes.Length})");
                arrayBuffer.WriteBytes(wasmBytes, 0, (ulong)wasmBytes.Length, 0);
                _engine.Script.pendingWasmBuffer = arrayBuffer;
            }

            _engine.Script.sourceCode = sourceCode;
            _engine.Script.targetStartLine = targetStartLine;
            _engine.Script.targetEndLine = targetEndLine;
            _engine.Script.langName = normalizedLanguage;

            var promise = _engine.Evaluate(@"
                (async () => {
                    try {
                        await loadLanguageIfNeeded(langName);
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

            if (!result.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                _jsLoadedLanguages.Add(normalizedLanguage);
            }

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