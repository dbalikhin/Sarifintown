importScripts('tree-sitter/tree-sitter.js');

let parser = null;
let languageCache = {};
let currentLanguage = null;

let languageMap = {
    'c': 'tree-sitter/tree-sitter-c.wasm',
    'csharp': 'tree-sitter/tree-sitter-c_sharp.wasm',
    'cpp': 'tree-sitter/tree-sitter-cpp.wasm',
    'go': 'tree-sitter/tree-sitter-go.wasm',
    'java': 'tree-sitter/tree-sitter-java.wasm',
    'javascript': 'tree-sitter/tree-sitter-javascript.wasm',
    'kotlin': 'tree-sitter/tree-sitter-kotlin.wasm',
    'php': 'tree-sitter/tree-sitter-php.wasm',
    'python': 'tree-sitter/tree-sitter-python.wasm',
    'ruby': 'tree-sitter/tree-sitter-ruby.wasm',
    'rust': 'tree-sitter/tree-sitter-rust.wasm',
    'typescript': 'tree-sitter/tree-sitter-typescript.wasm'
};

// Function to initialize Tree-sitter for a specific language
async function initialize(language) {
    try {
        if (!languageMap[language]) {
            throw new Error(`Language '${language}' not supported`);
        }

        if (languageCache[language]) {
            currentLanguage = languageCache[language];
        } else {
            await TreeSitter.init();
            currentLanguage = await TreeSitter.Language.load(languageMap[language]);
            languageCache[language] = currentLanguage;
        }

        parser = new TreeSitter();
        parser.setLanguage(currentLanguage);

    } catch (error) {
        postMessage({ status: 'error', message: error.message });
    }
}

async function lazyInitializeIfNeeded(language) {
    if (!parser || currentLanguage !== languageCache[language]) {
        await initialize(language);
    }
}

// Function to parse code for the selected language
async function parseCode(sourceCode, language) {
    try {
        await lazyInitializeIfNeeded(language);
        const tree = parser.parse(sourceCode);
        postMessage({ status: 'parsed', parseTree: tree.rootNode.toString() });
    } catch (error) {
        postMessage({ status: 'error', message: error.message });
    }
}

async function extractMethodByPosition(sourceCode, language, line, startColumn, endColumn, needAdjustment) {
    try {
        await lazyInitializeIfNeeded(language);
        const tree = parser.parse(sourceCode);
        
        const adjustedLine = needAdjustment ? line - 1 : line;
        const adjustedStartColumn = needAdjustment ? startColumn - 1 : startColumn;
        const adjustedEndColumn = needAdjustment ? endColumn - 1 : endColumn;

        const startPosition = { row: adjustedLine, column: adjustedStartColumn };
        const endPosition = { row: adjustedLine, column: adjustedEndColumn };

        let node = tree.rootNode.descendantForPosition(startPosition, endPosition);

        if (node) {
            console.log(`Found node: ${node.type}, startIndex: ${node.startIndex}, endIndex: ${node.endIndex}`);
        } else {
            console.log('No node found at the given position');
        }

        // Walk up the tree to find the enclosing method/function declaration
        while (node && !isMethodOrFunctionNode(node)) {
            node = node.parent;
        }

        if (node) {
            const originalText = sourceCode.substring(node.startIndex, node.endIndex);

            const sarifRegion = {
                startLine: node.startPosition.row + 1,
                endLine: node.endPosition.row + 1,
                startColumn: node.startPosition.column + 1,
                endColumn: node.endPosition.column + 1
            };
            console.log(JSON.stringify({ status: 'method_found', methodCode: originalText, sarifRegion: sarifRegion }));

            postMessage({ status: 'method_found', methodCode: originalText, sarifRegion: sarifRegion });
        } else {
            postMessage({ status: 'method_not_found', message: 'No method found at position' });
        }
    } catch (error) {
        postMessage({ status: 'error', message: error.message });
    }
}

// Helper function to check if the node is a method or function
function isMethodOrFunctionNode(node) {
    const methodOrFunctionTypes = [
        'method_declaration',       // C#, Java
        'function_declaration',     // JavaScript, C, C++
        'method_definition',        // JavaScript
        'function_definition',      // Python, Ruby
        'constructor_declaration',  // C#, Java
        'arrow_function',           // JavaScript
        'lambda_expression',        // C#, Python
        'function_item',            // Rust
        'function_expression',      // JavaScript, TypeScript
        'function_signature_item',  // Rust
        'function',                 // Generic for some languages
        'method_call',              // Common method call type
        'module_function_declaration', // Ruby
    ];

    return methodOrFunctionTypes.includes(node.type);
}

// Listen for messages from the main thread
onmessage = async function (e) {
    const { action, data } = e.data;
    switch (action) {
        case 'parse':
            parseCode(data.sourceCode, data.language);
            break;
        case 'extractByPosition':
            extractMethodByPosition(data.sourceCode, data.language, data.line, data.startColumn, data.endColumn, data.needAdjustment);
            break;
        default:
            postMessage({ status: 'error', message: 'Unknown action' });
    }
};


