# MCP Setup Guide for `Sarifintown.Engine`

This guide explains how to configure the `Sarifintown.Engine` as an MCP server in AI IDEs.

## Portable enforcement architecture (recommended)

Use these layers together to enforce consistent MCP behavior across GitHub Copilot, Visual Studio, JetBrains, and terminal clients without custom IDE extensions.

1. Workspace-level instruction files in repo root:
   - `.github/copilot-instructions.md` (create a similar "global" instruction for other IDEs)
2. Server-side MCP primitives:
   - aggressive `[Description]` text on guided MCP tools
   - native `[McpServerPrompt]` prompts for slash-command style workflow triggers
3. Chained tool payloads:
   - guided tools return explicit `next_step` metadata
4. Markdown pass-through payloads:
   - guided tools include an `llm_directive` requiring verbatim markdown render and user-input pause

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

---

## MCP stdio configuration (IDE schemas)

Different MCP clients use different top-level keys for server registration.

- Visual Studio / VS Code style: `"servers"`
- Cursor / Claude Code style: `"mcpServers"`

The server payload is the same in both styles (`transport`, `command`, `args`, optional `cwd`, optional `env`).

If your IDE already injects workspace context, you usually do not need to add `env` values manually.

### Visual Studio / VS Code style (`"servers"`)

```json
{
  "servers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "C:/path/to/your/workspace"
      }
    }
  }
}
```

macOS/Linux path variant:

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

### Cursor / Claude Code style (`"mcpServers"`)

```json
{
  "mcpServers": {
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

> Different IDEs may use different top-level keys, but the important values are the same: `transport`, `command`, `args`, and `env.PROJECT_ROOT`.

Common workspace placeholders in IDE configs include `${workspaceFolder}`, `{workspaceFolder}`, and `${workspaceRoot}`.
If a placeholder is passed through literally (not expanded), `sarifintown` ignores it and falls back to the next available workspace source.

If you cannot use the global tool in your IDE, use `dotnet run --project ...` as fallback.

### Client-specific examples

#### Cursor (`mcp.json`)

```json
{
  "mcpServers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "C:/path/to/your/workspace",
        "MCP_CLIENT_NAME": "Cursor"
      }
    }
  }
}
```

#### Claude Code (`mcp.json`)

```json
{
  "mcpServers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "env": {
        "PROJECT_ROOT": "/path/to/your/workspace",
        "MCP_CLIENT_NAME": "Claude Code"
      }
    }
  }
}
```

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

Most terminal-first MCP clients use `"mcpServers"`; if your client expects `"servers"`, keep the same server object and only switch the top-level key.

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

Use the same server body for both schema styles:

- wrap with `"servers"` for Visual Studio / VS Code style
- wrap with `"mcpServers"` for Cursor / Claude Code style

### Windows (preferred global tool)

```json
{
  "servers": {
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
  "servers": {
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
  "servers": {
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
- `env.PROJECT_ROOT`: recommended for deterministic `.sarif` discovery in multi-root or custom launch environments.
- `DOTNET_ENVIRONMENT`: optional; this project does not require it for MCP behavior.

### Path guidance

- Windows paths: `C:/...` (or escaped `C:\\...` depending on parser).
- macOS/Linux paths: `/...`.
- Prefer absolute paths for `cwd` and `PROJECT_ROOT`.

### Optional preload and warmup tuning

You can tune SARIF preload behavior and startup snippet warmup directly from MCP client env vars:

- `SARIFINTOWN_Sarif__Strategy`: `None`, `LatestPerTool`, or `All`
- `SARIFINTOWN_Sarif__EnableSnippetPreload`: `true`/`false`
- `SARIFINTOWN_Sarif__EnableDebugPrompt`: `true`/`false` (default `false`)
- `SARIFINTOWN_Sarif__IncludeEvidenceByDefault`: `true`/`false` (default `true`)

`SARIFINTOWN_Sarif__EnableDebugPrompt` and `SARIFINTOWN_Sarif__IncludeEvidenceByDefault` are evaluated only at MCP server startup. They cannot be changed from MCP prompts or slash-command arguments.

Snippet preload bootstrap is fixed to the first 10 findings during startup. Remaining findings are preloaded in the background.

Example (global tool invocation preserved):

```json
{
  "servers": {
    "sarifintown": {
      "transport": "stdio",
      "command": "sarifintown",
      "args": [],
      "cwd": "C:/dev/sarifintown",
      "env": {
        "PROJECT_ROOT": "C:/dev/my-app",
        "SARIFINTOWN_Sarif__Strategy": "LatestPerTool",
        "SARIFINTOWN_Sarif__EnableSnippetPreload": "true",
        "SARIFINTOWN_Sarif__EnableDebugPrompt": "false",
        "SARIFINTOWN_Sarif__IncludeEvidenceByDefault": "true"
      }
    }
  }
}
```

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
7. Call `TriageStatus` to confirm triage state aggregation from `.sarif/triage.json`.
8. Call `TriageList` and `TriageInspect` for finding prioritization and evidence retrieval.

---

## MCP tools currently exposed by `Sarifintown.Engine`

### Discovery and routing

- `ListWorkspaceSarifFiles`
- `ResolveInteractiveSurface`

### Analysis and extraction

- `LoadAndFilterSarif`
- `ExtractCodeFlow`
- `GenerateAnalysisReport`

### Triage workflow

- `TriageStatus`
- `TriageList`
- `TriageQuery` (alias of `TriageList`)
- `TriageInspect`
- `Triage`
- `TriageBulk`

### Guided triage workflow (recommended for autonomous agents)

- `TriageStatusGuided`
- `TriageListGuided`
- `TriageInspectGuided`

Guided tool responses include:

- `llm_directive` for markdown pass-through behavior
- `next_step` metadata for deterministic tool chaining
- `pause` metadata to force user-input checkpoints

### MCP prompts

- `SarifintownForceCheck`
- `SarifintownInspectFinding`

Triage decisions are persisted to `<workspace>/.sarif/triage.json`.

Note: Tool responses are JSON serialized from .NET types and therefore use `PascalCase` property names unless otherwise noted.

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

## Server starts but MCP `initialize` appears stuck

`Sarifintown.Engine` now emits startup diagnostics to **stderr** (MCP console output) so stdio protocol messages on stdout are not polluted.

Typical startup sequence:

1. Workspace discovery (`PROJECT_ROOT` and fallbacks)
2. Tree-sitter initialization
3. SARIF state initialization
4. Optional snippet preload
5. Web host start + local UI URL resolution
6. Wait for MCP traffic

If `initialize` stalls, inspect MCP console logs for lines prefixed with `sarifintown-mcp` and look for a `failed after ... ms` message to identify the blocking stage.

If the process crashes before your MCP client can render console output, run the same command directly in a terminal to capture stderr:

- Global tool: `sarifintown`
- Fallback: `dotnet run --project Sarifintown.AgentEngine/Sarifintown.Engine.csproj`
