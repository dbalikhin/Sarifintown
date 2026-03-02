# Sarifintown

Sarifintown is a Blazor WebAssembly solution for analyzing SARIF (Static Analysis Results Interchange Format) files and extracting code snippets from source code.

MCP server support is available via `Sarifintown.Engine`; see the concise setup guide in [`MCP_SETUP.md`](MCP_SETUP.md).

## Projects

- **Sarifintown.UI**: Blazor WebAssembly app for SARIF analysis, triage, and code snippet extraction.
- **Sarifintown.Core**: Shared models, helpers, and SARIF processing logic.
- **Sarifintown.Engine**: MCP server and CLI/TUI entrypoint for agent and terminal workflows.
- **Sarifintown.Tests**, **Sarifintown.Core.Tests**, **Sarifintown.Engine.Tests**: NUnit test projects.

## Features

- WASM Standalone application that works in your browser (Chromium-based browsers).
- Uses Browser File System API to read source code files (read-only access)
- Import SARIF files via drag-and-drop or file picker.
- Extract and highlight code snippets for each finding with PrismJS.
- Show full code flows.
- Extract whole methods with Tree-sitter WASM grammars to improve code flow analysis.
- Responsive UI built with MudBlazor.
- Group and filter results by severity, rule and file path.
- MCP tools for SARIF discovery, filtering, flow extraction, analysis report generation, and triage workflows.
- Shared triage state persisted in `.sarif/triage.json` for MCP/CLI triage commands.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
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

## MCP triage workflow commands

`Sarifintown.Engine` exposes a shared triage workflow through MCP tools:

- `TriageStatus`
- `TriageList` (and alias `TriageQuery`)
- `TriageInspect`
- `Triage`
- `TriageBulk`

These commands use loaded SARIF findings and persist decision state to `.sarif/triage.json`.

## License

This project is licensed under the Apache 2.0 License.
