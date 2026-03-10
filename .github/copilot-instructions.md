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
- Never use generic tools (custom scripts, terminal commands, or external summarization) to process SARIF triage or SARIF analysis data when MCP actions are available.

## SARIF Tool Workflow

### Reading findings: `sarif_get`
- Use `sarif_get` to retrieve the current posture summary and prioritized findings.
- Output the `<vulnerability_report>` block VERBATIM. Do NOT summarize, interpret, or duplicate.
- STOP after output and wait for the user's explicit instruction.
- Use `sarif_filter` to change the active scope before calling `sarif_get`.

### Autotriaging findings: `sarif_review`
- Use `sarif_review` for AI-driven autotriage of findings.
- Always call `sarif_get` first to load findings with evidence before calling `sarif_review`.
- Analyze the evidence (code flow, snippets, rule description, severity) and determine a decision state (`confirmed`, `false_positive`, `test_code`, `wont_fix`, `mitigated`) and reason.
- Pass your full chain-of-thought as `llmReasoning` and the evidence as `inputMarkdown`.
- Output the result VERBATIM and STOP.

### Filtering findings: `sarif_filter`
- Use `sarif_filter` to set or clear the active scope (severity, rule, path, status).
- Call with no arguments to list available filter values.
- After applying a filter, call `sarif_get` to view the filtered results.

### Manual override: `sarif_update`
- Use `sarif_update` only when the user explicitly asks to manually set or override a triage decision.
- Requires explicit `state`, `reason`, and `target` from the user.
- Sets `human_reviewed=true` in the audit ledger.
