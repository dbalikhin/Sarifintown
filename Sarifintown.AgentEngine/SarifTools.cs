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
        private static List<string> _discoveredSarifFiles = new();
        private static string _localUiBaseUrl = string.Empty;
        private static string _workspaceRoot = Directory.GetCurrentDirectory();
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
            }
        }

        [McpServerTool]
        [Description("MUST: Use this facade for all triage workflows. Set action to one of: status, list, inspect, decide, or bulk_decide.")]
        public static async Task<CallToolResult> manage_triage(
            [Description("Routing action: status, list, inspect, decide, or bulk_decide.")]
            string action,
            [Description("Finding identifier. Required for inspect and decide actions.")]
            string findingId = "",
            [Description("Decision state TP/FP. Required for decide and bulk_decide actions.")]
            string state = "",
            [Description("Decision reason. Required for decide and bulk_decide actions.")]
            string reason = "",
            [Description("Optional JSON object for filters and options (severity, rule, file, limit, guided, dryRun, evidenceMode, author).")]
            string filters = "",
            [Description("Execution mode: interactive or agentic. Interactive pauses for user input; agentic enforces autonomous next-step chaining.")]
            string mode = "interactive")
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("Action is required.", nameof(action));
            }

            var filterOptions = ParseFacadeFilters(filters);
            var normalizedAction = action.Trim().ToLowerInvariant();
            var normalizedMode = mode?.Trim().ToLowerInvariant();
            var useGuidedFlow = filterOptions.Guided || string.Equals(normalizedMode, "agentic", StringComparison.Ordinal);
            var author = string.IsNullOrWhiteSpace(filterOptions.Author) ? "AI" : filterOptions.Author;
            var ruleFilter = string.IsNullOrWhiteSpace(filterOptions.Rule) ? filterOptions.RuleId : filterOptions.Rule;

            if ((normalizedAction is "sql_issues" or "sqlissues" or "sqli") && string.IsNullOrWhiteSpace(ruleFilter))
            {
                ruleFilter = "sqli";
            }

            if (string.Equals(normalizedAction, "bulk_decide_complete", StringComparison.Ordinal))
            {
                var completedPayload = JsonSerializer.Serialize(new { success = true, message = "Bulk triage complete." });
                return CreateDualPurposeResult(
                    workflow: "Triage",
                    action: normalizedAction,
                    payload: completedPayload,
                    resourceUri: BuildUiResourceUri("triage", normalizedAction, string.Empty),
                    nextActionHint: "Task complete.");
            }

            string payload;
            string nextActionHint;
            string resourceId = string.Empty;

            switch (normalizedAction)
            {
                case "status":
                    payload = useGuidedFlow ? await TriageStatusGuided() : await TriageStatus();
                    nextActionHint = "Call manage_triage with action='list' to review prioritized findings.";
                    break;

                case "list":
                case "sql_issues":
                case "sqlissues":
                case "sqli":
                    payload = useGuidedFlow
                        ? await TriageListGuided(filterOptions.Severity, ruleFilter, filterOptions.File, filterOptions.State, filterOptions.Limit)
                        : await TriageList(filterOptions.Severity, ruleFilter, filterOptions.File, filterOptions.State, filterOptions.Limit);
                    nextActionHint = "Call manage_triage with action='inspect' and findingId='<id>' to review technical evidence.";
                    break;

                case "inspect":
                    if (string.IsNullOrWhiteSpace(findingId))
                    {
                        throw new ArgumentException("findingId is required for inspect action.", nameof(findingId));
                    }

                    resourceId = findingId;
                    payload = useGuidedFlow
                        ? await TriageInspectGuided(findingId, filterOptions.EvidenceMode)
                        : await TriageInspect(findingId, filterOptions.EvidenceMode);
                    nextActionHint = "Call manage_triage with action='decide', findingId='<id>', state='TP|FP', and reason='<required>'.";
                    break;

                case "decide":
                    if (string.IsNullOrWhiteSpace(findingId))
                    {
                        throw new ArgumentException("findingId is required for decide action.", nameof(findingId));
                    }

                    if (string.IsNullOrWhiteSpace(state))
                    {
                        throw new ArgumentException("state is required for decide action.", nameof(state));
                    }

                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        throw new ArgumentException("reason is required for decide action.", nameof(reason));
                    }

                    resourceId = findingId;
                    payload = await Triage(findingId, state, reason, author);
                    nextActionHint = "Call manage_triage with action='status' or action='list' to continue triage.";
                    break;

                case "bulk_decide":
                    if (string.IsNullOrWhiteSpace(state))
                    {
                        throw new ArgumentException("state is required for bulk_decide action.", nameof(state));
                    }

                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        throw new ArgumentException("reason is required for bulk_decide action.", nameof(reason));
                    }

                    payload = await TriageBulk(
                        state,
                        reason,
                        filterOptions.Severity,
                        ruleFilter,
                        filterOptions.File,
                        filterOptions.DryRun,
                        author);

                    nextActionHint = filterOptions.DryRun
                        ? "Call manage_triage with action='bulk_decide' and dryRun=false to persist decisions."
                        : "Call manage_triage with action='status' to review updated triage posture.";
                    break;

                default:
                    throw new ArgumentException("Action must be one of: status, list, inspect, decide, bulk_decide, sql_issues.", nameof(action));
            }

            return CreateDualPurposeResult(
                workflow: "Triage",
                action: normalizedAction,
                payload: payload,
                resourceUri: BuildUiResourceUri("triage", normalizedAction, resourceId),
                nextActionHint: nextActionHint);
        }

        [Description("MUST: Use this tool to obtain authoritative triage posture from loaded SARIF findings and local .sarif/triage.json state.")]
        public static async Task<string> TriageStatus()
        {
            var workflow = CreateTriageWorkflowService();
            var status = await workflow.GetStatusAsync();
            return JsonSerializer.Serialize(status);
        }

        [Description("MUST: Start autonomous triage flow with this guided tool. Render markdown verbatim, then follow next_step; do not use terminal commands for SARIF-domain actions.")]
        public static async Task<string> TriageStatusGuided()
        {
            var workflow = CreateTriageWorkflowService();
            var status = await workflow.GetStatusAsync();

            var markdown = $"""
            ## SARIF Triage Status

            - Total findings: **{status.TotalFindings}**
            - Open findings: **{status.OpenCount}**
            - Triaged findings: **{status.TriagedCount}**
            - True positives: **{status.TruePositiveCount}**
            - False positives: **{status.FalsePositiveCount}**

            **Next action:** Reply with `list` to review prioritized findings.
            """;

            return CreateGuidedResponse(
                workflowName: "triage-status",
                data: status,
                markdown: markdown,
                nextTool: "manage_triage",
                nextToolArguments: new { action = "list", filters = "{\"guided\":true,\"limit\":10}" },
                pauseForUserInput: true,
                pausePrompt: "Reply with `list` to continue to prioritized findings.");
        }

        [Description("MUST: Use this tool to retrieve prioritized findings with filters; do not infer finding sets without calling it.")]
        public static async Task<string> TriageList(
            [Description("Optional severity filter (for example: High, Medium, Low).")]
            string severity = "",
            [Description("Optional rule-id or rule-name filter.")]
            string rule = "",
            [Description("Optional file path filter (supports wildcard patterns handled by workflow service).")]
            string file = "",
            [Description("Optional triage state filter (Open, TP, FP).")]
            string state = "",
            [Description("Maximum findings to return.")]
            int limit = 10)
        {
            var workflow = CreateTriageWorkflowService();
            var findings = await workflow.ListAsync(new TriageQueryOptions(severity, rule, file, state, limit));
            return JsonSerializer.Serialize(findings);
        }

        [Description("MUST: Use this guided listing tool for autonomous chaining; response includes enforced next_step and pause directives.")]
        public static async Task<string> TriageListGuided(
            [Description("Optional severity filter (for example: High, Medium, Low).")]
            string severity = "",
            [Description("Optional rule-id or rule-name filter.")]
            string rule = "",
            [Description("Optional file path filter.")]
            string file = "",
            [Description("Optional triage state filter (Open, TP, FP).")]
            string state = "",
            [Description("Maximum findings to return.")]
            int limit = 10)
        {
            var workflow = CreateTriageWorkflowService();
            var findings = await workflow.ListAsync(new TriageQueryOptions(severity, rule, file, state, limit));

            var markdown = BuildGuidedFindingsMarkdown(findings);
            return CreateGuidedResponse(
                workflowName: "triage-list",
                data: findings,
                markdown: markdown,
                nextTool: "manage_triage",
                nextToolArguments: new { action = "inspect", findingId = "<reply-with-finding-id>", filters = "{\"guided\":true,\"evidenceMode\":\"line-window-concatenated\"}" },
                pauseForUserInput: true,
                pausePrompt: "Reply with a FindingId from the table to inspect technical evidence.");
        }

        [Description("Use this query-named alias when the MCP client requires query-style tool naming; behavior matches TriageList.")]
        public static Task<string> TriageQuery(
            [Description("Optional severity filter.")]
            string severity = "",
            [Description("Optional rule-id or rule-name filter.")]
            string rule = "",
            [Description("Optional file path filter.")]
            string file = "",
            [Description("Optional triage state filter.")]
            string state = "",
            [Description("Maximum findings to return.")]
            int limit = 10)
        {
            return TriageList(severity, rule, file, state, limit);
        }

        [Description("MUST: Use this tool for authoritative technical evidence of one finding, including ordered data-flow and snippets.")]
        public static async Task<string> TriageInspect(
            [Description("Finding identifier returned by TriageList or TriageListGuided.")]
            string findingId,
            [Description("Optional evidence mode override (for example: line-window-strict, line-window-concatenated, tree-sitter-method).")]
            string evidenceMode = "")
        {
            var workflow = CreateTriageWorkflowService();
            var inspect = await workflow.InspectAsync(findingId, evidenceMode);

            if (inspect == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"Finding not found: {findingId}"
                });
            }

            return JsonSerializer.Serialize(inspect);
        }

        [Description("MUST: Use this guided inspection tool in autonomous workflows. Render markdown verbatim and execute explicit next_step triage action.")]
        public static async Task<string> TriageInspectGuided(
            [Description("Finding identifier returned by guided list output.")]
            string findingId,
            [Description("Optional evidence mode override.")]
            string evidenceMode = "")
        {
            var workflow = CreateTriageWorkflowService();
            var inspect = await workflow.InspectAsync(findingId, evidenceMode);

            if (inspect == null)
            {
                var notFoundMarkdown = $"""
                ## Finding Not Found

                No finding matched `{findingId}`.

                **Next action:** Run a filtered list again and select a valid FindingId.
                """;

                return CreateGuidedResponse(
                    workflowName: "triage-inspect",
                    data: new { success = false, message = $"Finding not found: {findingId}" },
                    markdown: notFoundMarkdown,
                    nextTool: "manage_triage",
                    nextToolArguments: new { action = "list", filters = "{\"guided\":true,\"limit\":10}" },
                    pauseForUserInput: true,
                    pausePrompt: "Reply with `list` to load findings, then provide a valid FindingId.");
            }

            var markdown = BuildGuidedInspectMarkdown(inspect);
            return CreateGuidedResponse(
                workflowName: "triage-inspect",
                data: inspect,
                markdown: markdown,
                nextTool: "manage_triage",
                nextToolArguments: new { action = "decide", findingId = inspect.FindingId, state = "TP|FP", reason = "<required>", filters = "{\"author\":\"AI\"}" },
                pauseForUserInput: true,
                pausePrompt: "Reply with `TP <reason>` or `FP <reason>` to record a triage decision.");
        }

        [Description("MUST: Use this tool to persist a TP/FP triage decision for one finding into .sarif/triage.json.")]
        public static async Task<string> Triage(
            [Description("Target finding identifier.")]
            string findingId,
            [Description("Decision state to persist (TP or FP; natural-language aliases are normalized by workflow service).")]
            string state,
            [Description("Required reason for the decision.")]
            string reason,
            [Description("Decision author label.")]
            string author = "AI")
        {
            var workflow = CreateTriageWorkflowService();
            var result = await workflow.TriageAsync(findingId, state, reason, author);
            return JsonSerializer.Serialize(result);
        }

        [Description("MUST: Use this tool for bulk TP/FP triage updates using filters. At least one of severity/rule/file is required.")]
        public static async Task<string> TriageBulk(
            [Description("Decision state to apply (TP or FP).")]
            string state,
            [Description("Required reason applied to affected findings.")]
            string reason,
            [Description("Optional severity filter.")]
            string severity = "",
            [Description("Optional rule filter.")]
            string rule = "",
            [Description("Optional file filter.")]
            string file = "",
            [Description("When true, preview affected findings without persisting changes.")]
            bool dryRun = false,
            [Description("Decision author label.")]
            string author = "AI")
        {
            var workflow = CreateTriageWorkflowService();
            var result = await workflow.TriageBulkAsync(
                state,
                reason,
                new TriageQueryOptions(severity, rule, file, string.Empty, int.MaxValue),
                dryRun,
                author);

            return JsonSerializer.Serialize(result);
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
                        .ExecuteTriageActionAsync(CreateTriageWorkflowService(), selectedAction)
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

        private static FacadeFilterOptions ParseFacadeFilters(string filters)
        {
            if (string.IsNullOrWhiteSpace(filters))
            {
                return new FacadeFilterOptions();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<FacadeFilterOptions>(filters, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return parsed ?? new FacadeFilterOptions();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Filters must be a valid JSON object.", nameof(filters), ex);
            }
        }

        private static string GetWorkspaceRoot()
        {
            lock (SyncRoot)
            {
                return _workspaceRoot;
            }
        }

        private static CallToolResult CreateDualPurposeResult(
            string workflow,
            string action,
            string payload,
            string resourceUri,
            string nextActionHint)
        {
            var markdown = BuildPassThroughMarkdown(workflow, action, payload, nextActionHint);
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

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock
                    {
                        Text = markdown
                    }
                },
                Meta = meta
            };
        }

        private static string BuildPassThroughMarkdown(string workflow, string action, string payload, string nextActionHint)
        {
            return $"""
            [INSTRUCTIONS FOR LLM]
            You are acting as a UI renderer. Output the exact Markdown in the [CONTENT] block verbatim.
            Do NOT summarize it. After rendering, PAUSE and wait for the user to type the next command.

            [CONTENT]
            ### SARIF {workflow} {action}

            ```json
            {payload}
            ```

            **Next Action:** {nextActionHint}
            """;
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

        private static string CreateGuidedResponse(
            string workflowName,
            object data,
            string markdown,
            string nextTool,
            object nextToolArguments,
            bool pauseForUserInput,
            string pausePrompt)
        {
            return JsonSerializer.Serialize(new
            {
                protocol = "sarifintown.guided.v1",
                workflow = workflowName,
                llm_directive = "INSTRUCTIONS FOR LLM: Output the following markdown verbatim. Do not summarize or rephrase. After rendering, respect pause.required.",
                markdown,
                next_step = new
                {
                    tool = nextTool,
                    arguments = nextToolArguments
                },
                pause = new
                {
                    required = pauseForUserInput,
                    prompt = pausePrompt
                },
                data
            });
        }

        private static string BuildGuidedFindingsMarkdown(IReadOnlyList<TriageListItem> findings)
        {
            if (findings.Count == 0)
            {
                return """
                ## Prioritized Findings

                No findings matched the provided filters.

                **Next action:** Adjust filters and call `TriageListGuided` again.
                """;
            }

            var lines = findings
                .Select(item => $"| `{EscapeMarkdown(item.FindingId)}` | `{EscapeMarkdown(item.Severity)}` | `{EscapeMarkdown(item.State)}` | `{EscapeMarkdown(item.RuleName)}` | `{EscapeMarkdown(item.FilePath)}`:{item.LineNumber?.ToString() ?? "?"} |")
                .ToList();

            return string.Join(Environment.NewLine,
            [
                "## Prioritized Findings",
                string.Empty,
                "| FindingId | Severity | State | Rule | Location |",
                "|---|---|---|---|---|",
                .. lines,
                string.Empty,
                "**Next action:** Reply with a `FindingId` value to inspect evidence.",
                "Then pause for user input."
            ]);
        }

        private static string BuildGuidedInspectMarkdown(TriageInspectResult inspect)
        {
            var evidenceRows = inspect.DataFlowEvidenceBlocks
                .Select(block => $"- Steps {block.StartStepIndex}-{block.EndStepIndex} in `{EscapeMarkdown(block.FilePath)}` ({block.StartLine?.ToString() ?? "?"}-{block.EndLine?.ToString() ?? "?"}) via `{EscapeMarkdown(block.Mode)}`")
                .ToList();

            if (evidenceRows.Count == 0)
            {
                evidenceRows.Add("- No data-flow evidence blocks were produced for this finding.");
            }

            return string.Join(Environment.NewLine,
            [
                "## Finding Inspection",
                string.Empty,
                $"- FindingId: `{EscapeMarkdown(inspect.FindingId)}`",
                $"- Rule: `{EscapeMarkdown(inspect.RuleId)}`",
                $"- Severity: `{EscapeMarkdown(inspect.Severity)}`",
                $"- State: `{EscapeMarkdown(inspect.State)}`",
                string.Empty,
                "### Evidence Blocks",
                .. evidenceRows,
                string.Empty,
                "**Next action:** Reply with `TP <reason>` or `FP <reason>` to store triage state.",
                "Then pause for user input."
            ]);
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

        private sealed class FacadeFilterOptions
        {
            public string Severity { get; init; } = string.Empty;
            public string Rule { get; init; } = string.Empty;
            public string RuleId { get; init; } = string.Empty;
            public string File { get; init; } = string.Empty;
            public string State { get; init; } = string.Empty;
            public int Limit { get; init; } = 10;
            public bool Guided { get; init; }
            public bool DryRun { get; init; }
            public string EvidenceMode { get; init; } = string.Empty;
            public string Author { get; init; } = string.Empty;
            public string Category { get; init; } = string.Empty;
            public string SourceCodeRoot { get; init; } = string.Empty;
            public string OutputPath { get; init; } = string.Empty;
            public string ExtractedFlowData { get; init; } = string.Empty;
        }

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
