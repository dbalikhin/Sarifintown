# Sarifintown

Sarifintown is a Blazor WebAssembly solution for analyzing SARIF (Static Analysis Results Interchange Format) files and extracting code snippets from source code. 

## Projects

- **Sarifintown**: Blazor WebAssembly app for SARIF analysis and code snippet extraction.
- **Sarifintown.Tests**: Test project using NUnit and Playwright for automated testing.

## Features

- WASM Standalone application that works in your browser (Chromium-based browsers).
- Uses Browser File System API to read source code files (read-only access)
- Import SARIF files via drag-and-drop or file picker.
- Extract and highlight code snippets for each finding with PrismJS.
- Show full code flows 
- Responsive UI built with MudBlazor.
- Group and filter results by severity, rule and file path.

Under development: Extract the whole methods with Tree-sitter WASM grammars to improve code flow analysis.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Chromium-based browsers (Edge, Chrome) for File System API support. Sorry Firefox.


## Usage

1. **Select Source Code Folder**  
   Use the "Select Source Code" button to grant read-only access to your source code directory. SARIF files in the root and `.sarif` subfolder are detected automatically.

2. **Import SARIF Files**  
   Drag and drop SARIF files or use the file picker to import additional analysis results.

3. **Analyze Results**  
   View findings grouped by severity and rule. Add code snippets via the Button. Inspect extracted code snippets.

4. **Full Details Analysis**
   If a SARIF file contains the code flow, you can view code threads and highlights using Tree-sitter grammars.

## License

This project is licensed under the Apache 2.0 License.
