window.TreeSitterInterop = {
    worker: null,
    parserCache: {},
    currentLanguage: null,

    initializeWorker: function () {        
        if (!this.worker) {
            // Initialize worker if it hasn't been created
            this.worker = new Worker('js/tree-sitter-worker.js');
        }     
    },

    parseCode: function (sourceCode, language) {
        return new Promise((resolve, reject) => {
            const handleMessage = function (e) {
                const { status, message, parseTree } = e.data;
                if (status === 'parsed') {
                    resolve(parseTree);
                } else if (status === 'error') {
                    reject(new Error(message));
                }
                window.TreeSitterInterop.worker.removeEventListener('message', handleMessage);
            };

            // Add message handler and post the 'parse' action to the worker
            window.TreeSitterInterop.worker.addEventListener('message', handleMessage);
            this.worker.postMessage({ action: 'parse', data: { sourceCode, language } });
        });
    },

    extractMethodBySnippetPosition: function (sourceCode, language, line, startColumn, endColumn, needAdjustment) {
        return new Promise((resolve, reject) => {
            const handleMessage = function (e) {
                const { status, message, methodCode, sarifRegion } = e.data;

                if (status === 'method_found') {
                    const result = {
                        methodCode: methodCode,
                        sarifRegion: sarifRegion,
                        isFound: true,
                    };

                    console.log("Sending to C#: ", result);
                    resolve(result);
                } else if (status === 'method_not_found') {
                    const result = {
                        methodCode: "",
                        sarifRegion: null,
                        isFound: false,
                    };
                    resolve(result);
                
                } else if (status === 'error') {
                    reject(new Error(message));
                }

                // Remove the event listener once we get a response
                window.TreeSitterInterop.worker.removeEventListener('message', handleMessage);
            };

            // Add message event listener to capture worker response
            this.worker.addEventListener('message', handleMessage);

            // Send the request to the worker with the proper data
            this.worker.postMessage({
                action: 'extractByPosition',
                data: {
                    sourceCode,
                    language,
                    line,
                    startColumn,
                    endColumn,
                    needAdjustment
                }
            });
        });
    },
};