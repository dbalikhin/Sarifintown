using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Options;
using Sarifintown.Core;
using Sarifintown.AgentEngine.Configuration;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sarifintown.AgentEngine
{
    [McpServerToolType]
    public static class SarifTools
    {
        // Dependencies to be injected at startup in Program.cs
        public static IFileReader? FileReader { get; set; }
        public static ITreeSitterEngine? TreeSitterEngine { get; set; }
        internal static SarifStateService? StateService { get; set; }
        internal static SnippetCacheService? SnippetCache { get; set; }
        internal static SnippetWarmupService? SnippetWarmupService { get; set; }
        private static readonly object SyncRoot = new();
        public const string StateContextDelimiter = "===SARIF_STATE_CONTEXT===";
        private static List<string> _discoveredSarifFiles = new();
        private static string _localUiBaseUrl = string.Empty;
        private static string _workspaceRoot = Directory.GetCurrentDirectory();
        private static ActiveScopeFilter _activeScope = new();
        private static readonly string[] IdeHostTokens =
        [
            "vscode",
            "visualstudiocode",
            "visualstudio",
            "cursor",
            "windsurf",
            "jetbrains",
            "rider",
            "zed",
            "eclipse",
            "intellij",
            "xcode"
        ];

        private static readonly string[] CliHostTokens =
        [
            "claudecode",
            "claude",
            "codex",
            "aider",
            "geminicli",
            "opencode",
            "terminal",
            "bash",
            "zsh",
            "fish",
            "powershell",
            "pwsh",
            "cmd",
            "windowsterminal",
            "iterm",
            "tmux",
            "kitty",
            "alacritty"
        ];

        private static readonly string[] VsCodeFamilyTokens =
        [
            "vscode",
            "visualstudiocode",
            "cursor",
            "windsurf"
        ];

        private static readonly string[] JetBrainsFamilyTokens =
        [
            "jetbrains",
            "rider",
            "intellij",
            "pycharm",
            "webstorm",
            "clion",
            "goland",
            "rubymine"
        ];

        public static void SetDiscoveredSarifFiles(IEnumerable<string> discoveredSarifFiles)
        {
            ArgumentNullException.ThrowIfNull(discoveredSarifFiles);

            lock (SyncRoot)
            {
                _discoveredSarifFiles = discoveredSarifFiles
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public static void SetLocalUiBaseUrl(string localUiBaseUrl)
        {
            lock (SyncRoot)
            {
                _localUiBaseUrl = localUiBaseUrl?.Trim() ?? string.Empty;
            }
        }

        public static void SetWorkspaceRoot(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
            }

            lock (SyncRoot)
            {
                _workspaceRoot = Path.GetFullPath(workspaceRoot);
                _activeScope = new ActiveScopeFilter();
            }
        }

        [McpServerTool(Name = "sarif.get")]
        [Description("Retrieves prioritized SARIF findings, manages persistent Active Scope, and returns scope metrics.")]
        public static async Task<CallToolResult> SarifGet(
            [Description("Scope action: keep, set, refine, or clear.")]
            string scope = "keep",
            [Description("Filter expression, for example: severity:high, rule:SQLI.")]
            string filter = "",
            [Description("When true, attach evidence blocks per finding.")]
            bool includeEvidence = false,
            [Description("Maximum findings to return.")]
            int limit = 10)
        {
            var payload = await ExecuteScopedGetAsync(scope, filter, includeEvidence, limit);
            var stateContext = new
            {
                context = new
                {
                    active_scope = payload.Context.ActiveScope,
                    metrics = new
                    {
                        total_in_scope = payload.Context.Metrics.TotalInScope,
                        returned_in_batch = payload.Context.Metrics.ReturnedInBatch,
                        remaining_in_scope = payload.Context.Metrics.RemainingInScope
                    }
                }
            };

            return CreateDualPurposeResult(
                markdown: BuildScopedGetMarkdown(payload),
                systemStateContext: stateContext,
                resourceUri: BuildUiResourceUri("triage", "sarif.get", string.Empty),
                additionalMeta: BuildScopedMeta(payload.Context));
        }

        [McpServerTool(Name = "sarif.triage")]
        [Description("Applies TP/FP decisions to a target finding, list of findings, or current Active Scope.")]
        public static async Task<CallToolResult> SarifTriage(
            [Description("Decision state: TP or FP.")]
            string state,
            [Description("Decision reason/audit note.")]
            string reason,
            [Description("Target: single finding id, CSV ids, or literal scope.")]
            string target)
        {
            var payload = await ExecuteScopedTriageAsync(state, reason, target, "AI");

            return CreateDualPurposeResult(
                markdown: BuildScopedTriageMarkdown(payload),
                systemStateContext: null,
                resourceUri: BuildUiResourceUri("triage", "sarif.triage", string.Empty),
                additionalMeta: null);
        }

        [Description("MUST: Use this tool to resolve the correct interactive surface for the connected host before launching UI/TUI experiences.")]
        public static string ResolveInteractiveSurface(
            [Description("Active MCP server instance; used for host detection.")]
            McpServer thisServer,
            [Description("Optional explicit host hint override (for example: Visual Studio Code, Cursor, Claude Code, Rider).")]
            string hostHint = "",
            [Description("When true for CLI mode, starts the Spectre.Console menu immediately.")]
            bool startCliMenu = false)
        {
            var host = DetectHost(thisServer, hostHint);
            var mode = ResolveHostMode(host);
            var hostFamily = ResolveHostFamily(host);
            var usedFallback = string.Equals(host, "unknown", StringComparison.OrdinalIgnoreCase);

            if (mode == "ide-ui")
            {
                return JsonSerializer.Serialize(new
                {
                    host,
                    mode,
                    host_family = hostFamily,
                    uri = "ui://sarifintown/mcp/dashboard",
                    local_http_ui = CreateLocalHttpUiPayload(),
                    bridge = new
                    {
                        transport = "postMessage",
                        channel = "sarifintown.mcp.v1"
                    },
                    fallback = new
                    {
                        mode = "cli-tui",
                        library = "Spectre.Console"
                    }
                });
            }

            string selectedAction = string.Empty;
            string commandResult = string.Empty;
            if (startCliMenu)
            {
                selectedAction = SpectreCliMenu.Start();
                if (selectedAction.StartsWith("Triage ", StringComparison.Ordinal))
                {
                    commandResult = SpectreCliMenu
                        .ExecuteTriageActionAsync(selectedAction)
                        .GetAwaiter()
                        .GetResult();
                }
            }

            return JsonSerializer.Serialize(new
            {
                host,
                mode,
                host_family = hostFamily,
                fallback_used = usedFallback,
                local_http_ui = CreateLocalHttpUiPayload(),
                tui = new
                {
                    library = "Spectre.Console",
                    action = selectedAction,
                    action_result = commandResult,
                    menu = "interactive"
                }
            });
        }

        private static async Task<ScopedGetPayload> ExecuteScopedGetAsync(string scope, string filter, bool includeEvidence, int limit)
        {
            var scopeAction = ParseScopeAction(scope);
            var parsedFilter = string.IsNullOrWhiteSpace(filter)
                ? new ActiveScopeFilter()
                : ParseScopeFilter(filter);

            if (scopeAction is ScopeAction.Set or ScopeAction.Refine && parsedFilter.IsEmpty)
            {
                throw new ArgumentException("filter is required when scope is set or refine.", nameof(filter));
            }

            var activeScope = GetActiveScope();

            switch (scopeAction)
            {
                case ScopeAction.Set:
                    activeScope = parsedFilter;
                    SetActiveScope(activeScope);
                    break;
                case ScopeAction.Refine:
                    activeScope = MergeScope(activeScope, parsedFilter);
                    SetActiveScope(activeScope);
                    break;
                case ScopeAction.Clear:
                    activeScope = new ActiveScopeFilter();
                    SetActiveScope(activeScope);
                    break;
            }

            var executionScope = scopeAction == ScopeAction.Keep && !parsedFilter.IsEmpty
                ? MergeScope(activeScope, parsedFilter)
                : activeScope;

            var workflow = CreateTriageWorkflowService();
            var batchLimit = limit <= 0 ? 10 : limit;

            var activeScopeFindings = await workflow.ListAsync(activeScope.ToQueryOptions(int.MaxValue));
            var executionFindings = await workflow.ListAsync(executionScope.ToQueryOptions(batchLimit));

            var findingRows = new List<ScopedFinding>(executionFindings.Count);
            foreach (var finding in executionFindings)
            {
                TriageInspectResult? evidence = null;
                if (includeEvidence)
                {
                    evidence = await workflow.InspectAsync(finding.FindingId, string.Empty);
                }

                findingRows.Add(new ScopedFinding(
                    finding.FindingId,
                    finding.Severity,
                    finding.State,
                    finding.RuleName,
                    evidence?.Message ?? finding.RuleName,
                    new ScopedLocation(finding.FilePath, finding.LineNumber),
                    evidence));
            }

            var metrics = new SarifGetMetrics(
                activeScopeFindings.Count,
                findingRows.Count,
                activeScopeFindings.Count(item => string.Equals(item.State, TriageFindingState.Open.ToString(), StringComparison.OrdinalIgnoreCase)));

            return new ScopedGetPayload(
                new ScopedContext(
                    "Results are filtered by the persistent Active Scope.",
                    ToScopeDictionary(activeScope),
                    new ScopedMetrics(metrics.TotalInScope, metrics.ReturnedInBatch, metrics.RemainingInScope)),
                findingRows);
        }

        private static async Task<ScopedTriagePayload> ExecuteScopedTriageAsync(
            string state,
            string reason,
            string target,
            string author)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                throw new ArgumentException("state is required.", nameof(state));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("reason is required.", nameof(reason));
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                throw new ArgumentException("target is required.", nameof(target));
            }

            var normalizedState = NormalizeStrictDecisionState(state);
            var workflow = CreateTriageWorkflowService();

            List<string> targetIds;
            if (string.Equals(target, "scope", StringComparison.OrdinalIgnoreCase))
            {
                var activeScope = GetActiveScope();
                var scopedFindings = await workflow.ListAsync(activeScope.ToQueryOptions(int.MaxValue));
                targetIds = scopedFindings
                    .Where(item => string.Equals(item.State, TriageFindingState.Open.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.FindingId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
            else
            {
                targetIds = ResolveFindingIds(target);
            }

            var modifiedIds = new List<string>();
            foreach (var targetId in targetIds)
            {
                var decision = await workflow.TriageAsync(targetId, normalizedState, reason, author);
                if (decision.Success)
                {
                    modifiedIds.Add(targetId);
                }
            }

            return new ScopedTriagePayload(
                modifiedIds.Count == targetIds.Count,
                normalizedState,
                reason,
                target,
                modifiedIds.Count,
                modifiedIds);
        }

        private static string NormalizeStrictDecisionState(string state)
        {
            var normalized = state.Trim().ToUpperInvariant();
            if (normalized is "TP" or "FP")
            {
                return normalized;
            }

            throw new ArgumentException("state must be TP or FP.", nameof(state));
        }

        private static ScopeAction ParseScopeAction(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                return ScopeAction.Keep;
            }

            var normalized = scope.Trim().ToLowerInvariant();
            return normalized switch
            {
                "keep" => ScopeAction.Keep,
                "set" => ScopeAction.Set,
                "refine" => ScopeAction.Refine,
                "clear" => ScopeAction.Clear,
                _ => throw new ArgumentException("scope must be one of: keep, set, refine, clear.", nameof(scope))
            };
        }

        private static ActiveScopeFilter ParseScopeFilter(string filter)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in SplitFilterTokens(filter))
            {
                var separatorIndex = token.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
                {
                    continue;
                }

                var key = token[..separatorIndex].Trim();
                var value = token[(separatorIndex + 1)..].Trim().Trim('"', '\'');

                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    map[key] = value;
                }
            }

            map.TryGetValue("severity", out var severity);
            map.TryGetValue("rule", out var rule);

            if (string.IsNullOrWhiteSpace(rule) && map.TryGetValue("ruleId", out var ruleId))
            {
                rule = ruleId;
            }

            map.TryGetValue("file", out var file);
            if (string.IsNullOrWhiteSpace(file) && map.TryGetValue("path", out var path))
            {
                file = path;
            }

            map.TryGetValue("state", out var state);

            return new ActiveScopeFilter(severity ?? string.Empty, rule ?? string.Empty, file ?? string.Empty, state ?? string.Empty);
        }

        private static IReadOnlyList<string> SplitFilterTokens(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var buffer = new System.Text.StringBuilder();
            var inQuotes = false;

            foreach (var ch in filter)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    buffer.Append(ch);
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    var value = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
                    }

                    buffer.Clear();
                    continue;
                }

                buffer.Append(ch);
            }

            var trailing = buffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(trailing))
            {
                result.Add(trailing);
            }

            return result;
        }

        private static ActiveScopeFilter MergeScope(ActiveScopeFilter baseline, ActiveScopeFilter overlay)
        {
            return new ActiveScopeFilter(
                string.IsNullOrWhiteSpace(overlay.Severity) ? baseline.Severity : overlay.Severity,
                string.IsNullOrWhiteSpace(overlay.Rule) ? baseline.Rule : overlay.Rule,
                string.IsNullOrWhiteSpace(overlay.File) ? baseline.File : overlay.File,
                string.IsNullOrWhiteSpace(overlay.State) ? baseline.State : overlay.State);
        }

        private static ActiveScopeFilter GetActiveScope()
        {
            lock (SyncRoot)
            {
                return _activeScope;
            }
        }

        private static void SetActiveScope(ActiveScopeFilter activeScope)
        {
            lock (SyncRoot)
            {
                _activeScope = activeScope;
            }
        }

        private static Dictionary<string, string> ToScopeDictionary(ActiveScopeFilter activeScope)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(activeScope.Severity))
            {
                result["severity"] = activeScope.Severity;
            }

            if (!string.IsNullOrWhiteSpace(activeScope.Rule))
            {
                result["rule"] = activeScope.Rule;
            }

            if (!string.IsNullOrWhiteSpace(activeScope.File))
            {
                result["file"] = activeScope.File;
            }

            if (!string.IsNullOrWhiteSpace(activeScope.State))
            {
                result["state"] = activeScope.State;
            }

            return result;
        }

        private static JsonObject BuildScopedMeta(ScopedContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return JsonSerializer.SerializeToNode(new
            {
                context = new
                {
                    notice = context.Notice,
                    active_scope = context.ActiveScope,
                    metrics = new
                    {
                        total_in_scope = context.Metrics.TotalInScope,
                        returned_in_batch = context.Metrics.ReturnedInBatch,
                        remaining_in_scope = context.Metrics.RemainingInScope
                    }
                }
            }) as JsonObject ?? new JsonObject();
        }

        private static string GetWorkspaceRoot()
        {
            lock (SyncRoot)
            {
                return _workspaceRoot;
            }
        }

        private static CallToolResult CreateDualPurposeResult(
            string markdown,
            object? systemStateContext,
            string resourceUri,
            JsonObject? additionalMeta = null)
        {
            var text = markdown?.Trim() ?? string.Empty;

            if (systemStateContext != null)
            {
                var contextJson = JsonSerializer.Serialize(systemStateContext);
                text = $"""
                {text}

                {StateContextDelimiter}
                {contextJson}
                """;
            }

            if (string.IsNullOrWhiteSpace(resourceUri))
            {
                string localUiBaseUrl;
                lock (SyncRoot)
                {
                    localUiBaseUrl = _localUiBaseUrl;
                }

                if (Uri.TryCreate(localUiBaseUrl, UriKind.Absolute, out var baseUri))
                {
                    var builder = new UriBuilder(baseUri)
                    {
                        Path = "mcp/dashboard"
                    };

                    resourceUri = builder.Uri.ToString();
                }
            }

            var meta = new JsonObject();
            if (!string.IsNullOrWhiteSpace(resourceUri))
            {
                var csp = BuildUiCsp(resourceUri);
                meta["ui"] = new JsonObject
                {
                    ["resourceUri"] = resourceUri,
                    ["csp"] = csp
                };
            }

            if (additionalMeta != null)
            {
                foreach (var property in additionalMeta)
                {
                    meta[property.Key] = property.Value?.DeepClone();
                }
            }

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock
                    {
                        Text = text
                    }
                },
                Meta = meta
            };
        }

        private static string BuildUiResourceUri(string routePrefix, string action, string id)
        {
            var localUi = CreateLocalHttpUiPayload();
            var uri = GetPropertyValue(localUi, "uri")?.ToString();

            if (!string.IsNullOrWhiteSpace(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var baseUri))
            {
                var builder = new UriBuilder(baseUri)
                {
                    Path = $"mcp/{routePrefix}/{action}",
                    Query = string.IsNullOrWhiteSpace(id)
                        ? string.Empty
                        : $"id={Uri.EscapeDataString(id)}"
                };

                return builder.Uri.ToString();
            }

            return string.Empty;
        }

        private static string BuildUiCsp(string resourceUri)
        {
            if (!Uri.TryCreate(resourceUri, UriKind.Absolute, out var absoluteUri))
            {
                return "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self';";
            }

            var origin = absoluteUri.GetLeftPart(UriPartial.Authority);
            return $"default-src 'none'; script-src 'self' {origin}; style-src 'self' {origin}; connect-src 'self' {origin}; img-src 'self' data: {origin};";
        }

        private static string BuildScopedGetMarkdown(ScopedGetPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            var metrics = payload.Context.Metrics;
            var findings = payload.Findings;

            var lines = new List<string>
            {
                "## SARIF Scoped Query",
                string.Empty,
                $"- Total in scope: **{metrics.TotalInScope}**",
                $"- Returned in batch: **{metrics.ReturnedInBatch}**",
                $"- Remaining in scope: **{metrics.RemainingInScope}**",
                string.Empty
            };

            if (findings.Count == 0)
            {
                lines.Add("No findings in current result set.");
            }
            else
            {
                lines.Add("| Id | Severity | State | Rule | Location |\n|---|---|---|---|---|");
                foreach (var finding in findings)
                {
                    lines.Add($"| `{EscapeMarkdown(finding.Id)}` | `{EscapeMarkdown(finding.Severity)}` | `{EscapeMarkdown(finding.State)}` | `{EscapeMarkdown(finding.Rule)}` | `{EscapeMarkdown(finding.Location.File)}`:{finding.Location.Line?.ToString() ?? "?"} |");
                }
            }

            lines.Add(string.Empty);
            lines.Add("**Next action:** Use `sarif.triage` with `state`, `reason`, and `target`.");

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildScopedTriageMarkdown(ScopedTriagePayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            return string.Join(Environment.NewLine,
            [
                "## SARIF Scoped Triage",
                string.Empty,
                $"- Success: **{payload.Success}**",
                $"- State: `{EscapeMarkdown(payload.State)}`",
                $"- Target: `{EscapeMarkdown(payload.Target)}`",
                $"- Affected findings: **{payload.AffectedCount}**",
                string.Empty,
                "**Next action:** Run `sarif.get` to verify remaining findings in scope."
            ]);
        }

        private static List<string> ResolveFindingIds(string findingIds)
        {
            if (string.IsNullOrWhiteSpace(findingIds))
            {
                return new List<string>();
            }

            return findingIds
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string EscapeMarkdown(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace("|", "\\|", StringComparison.Ordinal);
        }

        private static TriageWorkflowService CreateTriageWorkflowService()
        {
            if (FileReader == null || TreeSitterEngine == null)
            {
                throw new InvalidOperationException("Core engines are not initialized.");
            }

            List<string> discoveredFiles;
            string workspaceRoot;
            var stateService = StateService;
            var snippetCache = SnippetCache;
            var snippetWarmupService = SnippetWarmupService;

            lock (SyncRoot)
            {
                discoveredFiles = _discoveredSarifFiles.ToList();
                workspaceRoot = _workspaceRoot;
            }

            stateService ??= new SarifStateService(
                FileReader,
                Options.Create(new SarifPreloadOptions
                {
                    Strategy = PreloadStrategy.LatestPerTool,
                    EnableSnippetPreload = false,
                    MaxPreloadedSnippets = 0
                }),
                workspaceRoot,
                discoveredFiles);

            snippetCache ??= new SnippetCacheService();

            return new TriageWorkflowService(
                FileReader,
                TreeSitterEngine,
                stateService,
                workspaceRoot,
                snippetCache,
                snippetWarmupService);
        }

        private static string DetectHost(McpServer thisServer, string hostHint)
        {
            if (!string.IsNullOrWhiteSpace(hostHint))
            {
                return hostHint.Trim();
            }

            if (thisServer != null)
            {
                var hostFromServer = TryGetHostNameFromServer(thisServer);
                if (!string.IsNullOrWhiteSpace(hostFromServer))
                {
                    return hostFromServer;
                }
            }

            var hostFromEnvironment = TryGetHostNameFromEnvironment();
            if (!string.IsNullOrWhiteSpace(hostFromEnvironment))
            {
                return hostFromEnvironment;
            }

            return "unknown";
        }

        private static string ResolveHostMode(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return "cli-tui";
            }

            var normalizedHost = NormalizeHost(host);

            if (ContainsAnyToken(normalizedHost, IdeHostTokens))
            {
                return "ide-ui";
            }

            if (ContainsAnyToken(normalizedHost, CliHostTokens))
            {
                return "cli-tui";
            }

            return "cli-tui";
        }

        private static string ResolveHostFamily(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return "terminal-family";
            }

            var normalizedHost = NormalizeHost(host);

            if (ContainsAnyToken(normalizedHost, VsCodeFamilyTokens))
            {
                return "vscode-family";
            }

            if (ContainsAnyToken(normalizedHost, JetBrainsFamilyTokens))
            {
                return "jetbrains-family";
            }

            if (normalizedHost.Contains("visualstudio", StringComparison.Ordinal))
            {
                return "visualstudio-family";
            }

            if (normalizedHost.Contains("zed", StringComparison.Ordinal))
            {
                return "zed-family";
            }

            if (ContainsAnyToken(normalizedHost, CliHostTokens))
            {
                return "terminal-family";
            }

            return "unknown-family";
        }

        private static string TryGetHostNameFromEnvironment()
        {
            var directValueVariables = new[]
            {
                "MCP_CLIENT_NAME",
                "MCP_HOST",
                "MCP_CLIENT",
                "TERM_PROGRAM",
                "TERM",
                "TERMINAL_EMULATOR",
                "PROMPT_TOOLKIT_SHELL",
                "VSCODE_CWD",
                "ELECTRON_RUN_AS_NODE"
            };

            foreach (var variable in directValueVariables)
            {
                var value = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDECODE")))
            {
                return "Claude Code";
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CURSOR_TRACE_ID")))
            {
                return "Cursor";
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VSCODE_GIT_IPC_HANDLE")))
            {
                return "Visual Studio Code";
            }

            return string.Empty;
        }

        private static string NormalizeHost(string host)
        {
            var lowered = host.Trim().ToLowerInvariant();

            return string.Concat(lowered.Where(char.IsLetterOrDigit));
        }

        private static bool ContainsAnyToken(string normalizedHost, IEnumerable<string> tokens)
        {
            return tokens.Any(token => normalizedHost.Contains(token, StringComparison.Ordinal));
        }

        private static string TryGetHostNameFromServer(McpServer server)
        {
            var directClientInfo = GetPropertyValue(server, "ClientInfo");
            var directName = GetPropertyValue(directClientInfo, "Name")?.ToString();

            if (!string.IsNullOrWhiteSpace(directName))
            {
                return directName.Trim();
            }

            var initializeRequest = GetPropertyValue(server, "InitializeRequest");
            var requestClientInfo = GetPropertyValue(initializeRequest, "ClientInfo");
            var requestClientName = GetPropertyValue(requestClientInfo, "Name")?.ToString();

            if (!string.IsNullOrWhiteSpace(requestClientName))
            {
                return requestClientName.Trim();
            }

            var session = GetPropertyValue(server, "Session");
            var sessionClientInfo = GetPropertyValue(session, "ClientInfo");
            var sessionClientName = GetPropertyValue(sessionClientInfo, "Name")?.ToString();

            return string.IsNullOrWhiteSpace(sessionClientName)
                ? string.Empty
                : sessionClientName.Trim();
        }

        private static object CreateLocalHttpUiPayload()
        {
            string baseUrl;
            lock (SyncRoot)
            {
                baseUrl = _localUiBaseUrl;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new
                {
                    available = false,
                    uri = (string?)null
                };
            }

            return new
            {
                available = true,
                uri = $"{baseUrl.TrimEnd('/')}/mcp/dashboard"
            };
        }

        private static object? GetPropertyValue(object? instance, string propertyName)
        {
            if (instance == null)
            {
                return null;
            }

            var property = instance
                .GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property == null)
            {
                return null;
            }

            return property.GetValue(instance);
        }

        private sealed record ScopedMetrics(int TotalInScope, int ReturnedInBatch, int RemainingInScope);

        private sealed record ScopedContext(
            string Notice,
            IReadOnlyDictionary<string, string> ActiveScope,
            ScopedMetrics Metrics);

        private sealed record ScopedLocation(string File, int? Line);

        private sealed record ScopedFinding(
            string Id,
            string Severity,
            string State,
            string Rule,
            string Message,
            ScopedLocation Location,
            TriageInspectResult? Evidence);

        private sealed record ScopedGetPayload(ScopedContext Context, IReadOnlyList<ScopedFinding> Findings);

        private sealed record ScopedTriagePayload(
            bool Success,
            string State,
            string Reason,
            string Target,
            int AffectedCount,
            IReadOnlyList<string> ModifiedFindingIds);

        [Description("MUST: Use this tool to compile extracted flow JSON into a markdown report artifact for downstream analysis.")]
        public static string GenerateAnalysisReport(
            [Description("Result identifier for report metadata.")]
            string resultId,
            [Description("Extracted flow JSON payload returned by ExtractCodeFlow.")]
            string extractedFlowData,
            [Description("Destination markdown file path.")]
            string outputPath)
        {
            try
            {
                var flowData = JsonSerializer.Deserialize<JsonElement>(extractedFlowData);
                string ruleId = flowData.TryGetProperty("rule_id", out var ruleElement) ? ruleElement.GetString() ?? "Unknown_Rule" : "Unknown_Rule";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# Vulnerability Analysis Report");
                sb.AppendLine($"**Rule ID:** {ruleId}");
                sb.AppendLine($"**Result Index:** {resultId}");
                sb.AppendLine($"**Date Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine("\n## Data Flow Context\n");

                if (flowData.TryGetProperty("flow_steps", out var stepsElement) && stepsElement.ValueKind == JsonValueKind.Array)
                {
                    int stepNum = 1;
                    foreach (var step in stepsElement.EnumerateArray())
                    {
                        string file = step.GetProperty("file_path").GetString() ?? "unknown_file";
                        int line = step.TryGetProperty("start_line", out var lineElem) ? lineElem.GetInt32() : 0;
                        string snippet = step.GetProperty("code_snippet").GetString() ?? "";
                        string msg = step.TryGetProperty("message", out var msgElement) ? msgElement.GetString() ?? "" : "";

                        sb.AppendLine($"### Step {stepNum}: {file} (Line {line})");
                        if (!string.IsNullOrEmpty(msg)) sb.AppendLine($"*Context:* {msg}\n");
                        sb.AppendLine("```csharp");
                        sb.AppendLine(snippet);
                        sb.AppendLine("```\n");
                        stepNum++;
                    }
                }
                else
                {
                    sb.AppendLine("*No valid data flow steps extracted.*");
                }

                File.WriteAllText(outputPath, sb.ToString());

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Report generated successfully.",
                    file_path = Path.GetFullPath(outputPath)
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to generate report: {ex.Message}" });
            }
        }
    }
}
