# MCP Setup Guide

This guide explains how to configure `sarifintown` as an MCP server in AI IDEs and terminal clients.

## What the server does at startup

1. Resolves workspace root from environment variables (in order):
   - `PROJECT_ROOT`, `WORKSPACE_FOLDER`, `WORKSPACE_ROOT`, `MCP_WORKSPACE_ROOT`, `PWD`
   - Falls back to current working directory
   - Ignores unresolved placeholders such as `${workspaceFolder}` or `{workspaceFolder}`
2. Scans `<workspace>/.sarif/` recursively for `*.sarif` files
3. Initializes Tree-sitter, SARIF state, and snippet preload
4. Starts local web host and waits for MCP traffic

In most IDEs this works automatically from the open folder/workspace context.

---

## Workspace layout

```text
<your-repo>/
  .sarif/
    scan1.sarif
    security/results.sarif
```

---

## Running the server

**Option A (preferred):** global tool

```bash
sarifintown
```

**Option B:** from source

```bash
dotnet run --project Sarifintown.AgentEngine/Sarifintown.Engine.csproj
```

---

## MCP configuration

Different clients use different top-level keys: `"servers"` (Visual Studio, VS Code) or `"mcpServers"` (Cursor, Claude Code). The server body is the same.

### Minimal config

```json
{
  "servers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "/path/to/your/workspace"
      }
    }
  }
}
```

> For Cursor or Claude Code, replace `"servers"` with `"mcpServers"`.

### Field reference

| Field | Required | Notes |
|---|---|---|
| `transport` | Yes | Must be `stdio`. |
| `command` | Yes | `sarifintown` (global tool) or `dotnet` with `args: ["run", "--project", "..."]`. |
| `env.PROJECT_ROOT` | Recommended | Absolute path. Ensures deterministic `.sarif` discovery. |
| `env.MCP_CLIENT_NAME` | Optional | Improves host detection (e.g. `"Cursor"`, `"Claude Code"`). Also recognized: `MCP_HOST`, `MCP_CLIENT`. |
| `cwd` | Optional | Useful for predictable relative paths. |

### Fallback config (dotnet run)

```json
{
  "servers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/Sarifintown.AgentEngine/Sarifintown.Engine.csproj"
      ],
      "env": {
        "PROJECT_ROOT": "/path/to/your/workspace"
      }
    }
  }
}
```

### Path guidance

- Windows: `C:/...` (or escaped `C:\\...` depending on JSON parser).
- macOS/Linux: `/...`.
- Prefer absolute paths for `PROJECT_ROOT` and `cwd`.
- Common IDE placeholders (`${workspaceFolder}`, `${workspaceRoot}`) are ignored if passed literally.

---

## Terminal usage

For terminal-first clients (Claude Code, Codex CLI, Aider):

```bash
# macOS/Linux
export PROJECT_ROOT="/path/to/your/workspace"
sarifintown
```

```powershell
# Windows PowerShell
$env:PROJECT_ROOT = "C:/path/to/your/workspace"
sarifintown
```

---

## Optional environment variables

Tune SARIF preload behavior via env vars:

| Variable | Values | Default |
|---|---|---|
| `SARIFINTOWN_Sarif__Strategy` | `None`, `LatestPerTool`, `All` | `LatestPerTool` |
| `SARIFINTOWN_Sarif__EnableSnippetPreload` | `true` / `false` | `true` |

Example:

```json
{
  "servers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "/path/to/workspace",
        "SARIFINTOWN_Sarif__Strategy": "LatestPerTool",
        "SARIFINTOWN_Sarif__EnableSnippetPreload": "true"
      }
    }
  }
}
```

---

## Quick validation checklist

1. Ensure `PROJECT_ROOT/.sarif/` exists with at least one `*.sarif` file.
2. Restart IDE MCP servers.
3. Call `sarif_get` to view findings.
4. Call `sarif_filter` to set scope if needed.
5. Call `sarif_review` with a target displayid to load evidence.
6. Call `sarif_update` to record a triage decision.
7. Call `sarif_sync` to push decisions to upstream vendors.

---

## MCP tools and prompts

### Tools

- `sarif_get` — retrieve paginated findings index
- `sarif_filter` — set or clear active scope filters
- `sarif_review` — load code-flow evidence and triage rules for a finding
- `sarif_update` — record a triage decision (AI or human)
- `sarif_sync` — push pending decisions to upstream vendor APIs

Tool responses include embedded `next_step` metadata for workflow chaining.

### Prompts

Slash-command prompts mirror each tool: `sarif_filter`, `sarif_get`, `sarif_review`, `sarif_update`, `sarif_sync`.

### Persistence

- Triage state: `<workspace>/.sarif/triage.json`
- Audit ledger: `<workspace>/.sarif/triage-ledger.json`
- Execution log: `<workspace>/.sarif/agent-execution.log`

---

## Troubleshooting

### No SARIF files returned

- Check `PROJECT_ROOT` points to the correct workspace.
- Confirm files are inside `.sarif/` with `.sarif` extension.

### Server fails to start

- Test the command manually in a terminal.
- Verify .NET 10 SDK is installed.
- Verify project path is correct.

### Server starts but MCP `initialize` appears stuck

The server emits startup diagnostics to **stderr** so stdio protocol messages on stdout are not polluted.

Typical startup sequence:

1. Workspace discovery
2. Tree-sitter initialization
3. SARIF state initialization
4. Snippet preload
5. Web host start
6. Wait for MCP traffic

If `initialize` stalls, inspect MCP console logs for lines prefixed with `sarifintown-mcp` and look for a `failed after ... ms` message.

To capture stderr directly:

```bash
sarifintown
# or: dotnet run --project Sarifintown.AgentEngine/Sarifintown.Engine.csproj
```
