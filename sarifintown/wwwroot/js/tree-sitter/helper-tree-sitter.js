window.treeSitterWorker = null;

window.initializeWorker = function () {
    // Check if the worker is already initialized
    if (!window.treeSitterWorker) {
        window.treeSitterWorker = new Worker('treeSitterWorker.js');

        window.treeSitterWorker.onmessage = function (event) {
            const result = event.data;
            console.log("Parsed Tree:", result);

            // Call the Blazor static method
            DotNet.invokeMethodAsync('Sarifintown', 'HandleParsedTree', result);
        };

        window.treeSitterWorker.onerror = function (error) {
            console.error("Worker error:", error);
        };

        console.log("Web Worker initialized.");
    } else {
        console.log("Web Worker is already initialized.");
    }
};

window.parseCodeInWorker = function (sourceCode) {
    // Ensure the worker is initialized before calling postMessage
    if (window.treeSitterWorker) {
        window.treeSitterWorker.postMessage(sourceCode);
    } else {
        console.error("Web Worker has not been initialized.");
        initializeWorker();
    }
};