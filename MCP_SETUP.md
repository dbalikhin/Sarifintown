# MCP Setup Guide for `Sarifintown.Engine`

This guide explains how to configure the `Sarifintown.Engine` as an MCP server in AI IDEs.

## What the server does at startup

When the server starts, it:

1. Resolves workspace root from:
   - `PROJECT_ROOT`
   - `WORKSPACE_FOLDER`
   - `WORKSPACE_ROOT`
   - `MCP_WORKSPACE_ROOT`
   - `PWD`
   - otherwise current working directory
   - ignores unresolved placeholders such as `{workspaceFolder}` or `${workspaceFolder}`
2. Scans `<workspace>/.sarif/` recursively
3. Registers all `*.sarif` files for tool-based parsing

In most IDEs this works automatically from the open folder/workspace context.

---

## Workspace layout expected by the server

```text
<your-repo>/
  .sarif/
    scan1.sarif
    security/results.sarif
```

---

## Build/Run options

Preferred: use the .NET global tool command `sarifintown`.

## Option A (preferred): run via global tool

```bash
sarifintown
```

## Option B: run from source

```bash
dotnet run --project Sarifintown.AgentEngine/Sarifintown.Engine.csproj
```

## Option C: run with alternate tool command (if present in your environment)

```bash
sarifintown-agent
```

---

## Generic MCP stdio configuration (works with most AI IDEs)

Many IDEs use a JSON file (`mcp.json`, `settings.json`, or similar) with an MCP servers section.

If your IDE already injects workspace context, you usually do not need to add any `env` values manually.

Use this pattern (Windows example):

```json
{
  "mcpServers": {
    "sarifintown": {
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "C:/path/to/your/workspace"
      }
    }
  }
}
```

macOS/Linux example:

```json
{
  "mcpServers": {
    "sarifintown": {
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "/path/to/your/workspace"
      }
    }
  }
}
```

> Different IDEs may use different top-level keys, but the important values are the same: `command`, `args`, and `env.PROJECT_ROOT`.

Common workspace placeholders in IDE configs include `${workspaceFolder}`, `{workspaceFolder}`, and `${workspaceRoot}`.
If a placeholder is passed through literally (not expanded), `sarifintown` ignores it and falls back to the next available workspace source.

If you cannot use the global tool in your IDE, use `dotnet run --project ...` as fallback.

---

## Terminal MCP configuration (CLI clients)

If you use MCP from terminal-first clients (for example Claude Code, Codex CLI, Aider, or custom MCP CLI runners), use stdio transport and pass workspace via environment variables.

Minimum terminal launch pattern:

### Windows PowerShell

```powershell
$env:PROJECT_ROOT = "C:/path/to/your/workspace"
$env:MCP_CLIENT_NAME = "Claude Code"
sarifintown
```

### macOS/Linux

```bash
export PROJECT_ROOT="/path/to/your/workspace"
export MCP_CLIENT_NAME="Claude Code"
sarifintown
```

Notes:

- `PROJECT_ROOT` is strongly recommended so `.sarif` discovery is deterministic.
- `MCP_CLIENT_NAME` is optional but recommended; it improves host detection/routing.
- If your client sets `MCP_HOST` or `MCP_CLIENT`, those are also recognized.

### Example terminal client config (`mcp.json` style)

```json
{
  "mcpServers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "/absolute/path/to/workspace",
        "MCP_CLIENT_NAME": "Claude Code"
      }
    }
  }
}
```

### Fallback command for terminal clients

If `sarifintown` is not installed as a global tool:

```bash
dotnet run --project Sarifintown.AgentEngine/Sarifintown.Engine.csproj
```

---

## Detailed sample for a typical `mcp.json`

### Windows (preferred global tool)

```json
{
  "mcpServers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "cwd": "C:/dev/sarifintown",
      "env": {
        "PROJECT_ROOT": "C:/dev/my-app"
      }
    }
  }
}
```

### macOS/Linux (preferred global tool)

```json
{
  "mcpServers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "cwd": "/Users/me/dev/sarifintown",
      "env": {
        "PROJECT_ROOT": "/Users/me/dev/my-app"
      }
    }
  }
}
```

### Fallback sample (`dotnet run`)

```json
{
  "mcpServers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/dev/sarifintown/Sarifintown.AgentEngine/Sarifintown.Engine.csproj"
      ],
      "cwd": "C:/dev/sarifintown",
      "env": {
        "PROJECT_ROOT": "C:/dev/my-app"
      }
    }
  }
}
```

### Why each field matters

- `transport`: must be `stdio` for this server mode.
- `command` + `args`: starts the MCP server process.
- `cwd`: optional but useful for predictable relative paths.
- `env.PROJECT_ROOT`: **required** for correct SARIF discovery from `.sarif/`.
- `DOTNET_ENVIRONMENT`: optional; this project does not require it for MCP behavior.

### Path guidance

- Windows paths: `C:/...` (or escaped `C:\\...` depending on parser).
- macOS/Linux paths: `/...`.
- Prefer absolute paths for `cwd` and `PROJECT_ROOT`.

---

## Quick validation checklist

1. Ensure `PROJECT_ROOT/.sarif` exists.
2. Ensure at least one `*.sarif` file is present.
3. Restart IDE MCP servers.
4. Call `ListWorkspaceSarifFiles` from your MCP client.
5. Confirm files are returned.
6. Call `LoadAndFilterSarif` using either:
   - full SARIF path, or
   - filename discovered at startup (for example: `scan1.sarif`).

---

## IDE UI surface contract (MCP `ui://`)

`ResolveInteractiveSurface` returns:

- `uri`: `ui://sarifintown/mcp/dashboard`
- `bridge.transport`: `postMessage`
- `bridge.channel`: `sarifintown.mcp.v1`

Recommended message envelope for host ↔ UI:

```json
{
  "channel": "sarifintown.mcp.v1",
  "type": "host.ping",
  "requestId": "optional-correlation-id",
  "payload": {}
}
```

Common events:

- Host -> UI: `host.ping`, `host.getState`
- Host -> UI: `host.openFinding` (payload: `resultIdentity`)
- UI -> Host: `ui.ready`, `ui.pong`, `ui.state`, `ui.request.chatPrompt`

`ui.request.chatPrompt` payload:

```json
{
  "channel": "sarifintown.mcp.v1",
  "type": "ui.request.chatPrompt",
  "requestId": "optional-correlation-id",
  "payload": {
    "prompt": "Explain SQL injection at line 42 in auth.cs",
    "context": {
      "source": "Sarifintown.McpDashboard",
      "route": "/mcp/dashboard"
    }
  }
}
```

Recommended host response:

```json
{
  "channel": "sarifintown.mcp.v1",
  "type": "host.chatPrompt.ack",
  "requestId": "same-request-id",
  "payload": {
    "accepted": true
  }
}
```

---

## Troubleshooting

## No SARIF files returned

- Check `PROJECT_ROOT` points to the correct workspace.
- Confirm files are inside `.sarif/`.
- Confirm extension is `.sarif`.

## File not found from tool call

- Use `ListWorkspaceSarifFiles` first to verify discovered names.
- Pass exact filename or full path.

## Server fails to start

- Test the command manually in terminal.
- Verify .NET SDK is installed.
- Verify project path is correct.
