# Copilot Instructions

## Project Guidelines
- User prefers fewer dependencies.
- Always use explicit, fully qualified version numbers for NuGet packages in .csproj files and avoid wildcards or floating versions.
- User prefers standard NUnit assertions over FluentAssertions because FluentAssertions is not free.
- When documenting MCP setup for this project, do not require DOTNET_ENVIRONMENT in configuration unless explicitly needed by project behavior.

## MCP Enforcement Baseline
- For SARIF/SAST/Secret triaging and reviewing workflows, use only `Sarifintown.Engine` MCP tools. Do not substitute with generic shell/terminal commands.
- Do not guess repository state, scan results, triage state, or code-flow evidence. Retrieve data through MCP tools first.
- If a required domain action has an MCP tool, call the tool before proposing conclusions.
- Keep responses aligned with tool outputs; do not invent findings, IDs, or file paths.
