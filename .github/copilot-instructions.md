# Copilot Instructions

## Project Guidelines
- User prefers fewer dependencies.
- Always use explicit, fully qualified version numbers for NuGet packages in .csproj files and avoid wildcards or floating versions.
- User prefers standard NUnit assertions over FluentAssertions because FluentAssertions is not free.
- When documenting MCP setup for this project, do not require DOTNET_ENVIRONMENT in configuration unless explicitly needed by project behavior.

## Sarifintown MCP Tools

For SARIF/SAST/Secret triaging workflows, always use the sarifintown MCP tools (`sarif_filter`, `sarif_get`, `sarif_review`, `sarif_update`, `sarif_sync`). Do not substitute with generic shell commands, do not guess findings or IDs — retrieve data through tools first.

### Workflow: reviewing findings
1. `sarif_get` → retrieve paginated findings index. Output the `<vulnerability_report>` block verbatim, then stop.
2. `sarif_review(target)` → load code-flow evidence and triage rules for a displayid. Proceed with analysis immediately.
3. `sarif_update(target, state, reason, llmReasoning)` → record the triage decision. For AI triage, always provide `llmReasoning`. For human manual overrides, omit it.

### Workflow: filtering
- `sarif_filter(query)` → set scope (e.g. `severity:high rule:SQLI`). Call `sarif_get` after.
- `sarif_filter("clear")` → remove all filters.
- `sarif_filter()` with no arguments → list available filter values.

### Workflow: syncing
- `sarif_sync` → push pending triage decisions to upstream vendor APIs.
