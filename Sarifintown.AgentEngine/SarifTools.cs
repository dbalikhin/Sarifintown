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
        internal static IPromptAssemblyService? PromptAssembly { get; set; }
        private static readonly object SyncRoot = new();
        public const string StateContextDelimiter = "===SARIF_STATE_CONTEXT===";
        private const int MaxEvidenceInspectCount = 25;
        private static List<string> _discoveredSarifFiles = new();
        private static string _localUiBaseUrl = string.Empty;
        private static string _workspaceRoot = Directory.GetCurrentDirectory();
        private static ActiveScopeFilter _activeScope = new();
        private static string _paginationScopeKey = string.Empty;
        private static int _paginationNextOffset;
        private static readonly Dictionary<string, string> DisplayIdToFindingId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> FindingIdToDisplayId = new(StringComparer.Ordinal);
        private static int _nextDisplayId = 1;
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
                _paginationScopeKey = string.Empty;
                _paginationNextOffset = 0;
                ResetDisplayIdMappings();
            }
        }

        [McpServerTool(Name = "sarif_get")]
        [Description("Retrieve scoped SARIF findings. CRITICAL EXECUTION PROTOCOL: (1) Output exactly one vulnerability_report block VERBATIM from this tool result. (2) Do NOT summarize, interpret, restate, duplicate, or render additional tables. (3) STOP after output and wait for explicit user instruction. (4) Never call sarif_get again unless the user explicitly asks for another page/filter/scope change.")]
        public static async Task<CallToolResult> SarifGet(
            [Description("Scope action: keep (reuse active scope), set (replace scope), refine (narrow scope), or clear (remove all filters).")] 
            string scope = "keep",
            [Description("Filter expression, for example: severity:high, rule:SQLI, file:Controller.cs. Combine with commas.")]
            string filter = "",
            [Description("When true, attach evidence blocks and assembled triage prompt per finding.")]
            bool includeEvidence = false,
            [Description("Maximum findings to return (1-25).")]
            int limit = 10,
            [Description("Optional 1-based page number. When provided, this overrides automatic pagination and pageToken.")]
            int page = 0,
            [Description("When true, append the fully assembled triage prompt text to the output for debugging.")]
            bool debugPrompt = false,
            [Description("Optional pagination token returned by a previous sarif_get call. Use context.pagination.next_page_token to fetch the next batch.")]
            string pageToken = "")
        {
            var safeLimit = limit <= 0 ? 10 : Math.Min(limit, 25);
            var payload = await ExecuteScopedGetAsync(scope, filter, includeEvidence, safeLimit, page, debugPrompt, pageToken);
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
                    },
                    pagination = new
                    {
                        page_token = payload.Context.Pagination.PageToken,
                        page_size = payload.Context.Pagination.PageSize,
                        page_number = payload.Context.Pagination.PageNumber,
                        total_pages = payload.Context.Pagination.TotalPages,
                        has_more = payload.Context.Pagination.HasMore,
                        next_page_token = payload.Context.Pagination.NextPageToken,
                        previous_page_token = payload.Context.Pagination.PreviousPageToken,
                        next_page_number = payload.Context.Pagination.NextPageNumber,
                        previous_page_number = payload.Context.Pagination.PreviousPageNumber
                    },
                    aliases = payload.Findings
                        .Select(item => new { displayid = item.DisplayId, finding_id = item.FindingId })
                        .ToArray()
                }
            };

            var metaObj = BuildScopedMeta(payload);
            metaObj["pause"] = true;
            metaObj["next_step"] = "sarif_triage";

            return CreateDualPurposeResult(
                markdown: BuildScopedGetMarkdown(payload),
                systemStateContext: stateContext,
                resourceUri: BuildUiResourceUri("triage", "sarif_get", string.Empty),
                additionalMeta: metaObj);
        }

        [McpServerTool(Name = "sarif_triage")]
        [Description("Persist a triage decision for one or more findings. CRITICAL EXECUTION PROTOCOL: (1) Call this tool with a displayid from sarif_get output. (2) Output the result block VERBATIM. (3) Run sarif_get again to verify remaining findings. Do NOT apply baseline knowledge or guess finding state.")]
        public static async Task<CallToolResult> SarifTriage(
            [Description("Decision state: confirmed (true positive), false_positive (not a real issue), test_code (in test/non-production code), wont_fix (accepted risk), or mitigated (already addressed).")]
            string state,
            [Description("Required decision reason/audit note explaining why this decision was made.")]
            string reason,
            [Description("Target displayid (e.g. 1), CSV displayid list (e.g. 1,2,3), or literal 'scope' to triage all open findings in active scope.")]
            string target)
        {
            var payload = await ExecuteScopedTriageAsync(state, reason, target, "AI");

            return CreateDualPurposeResult(
                markdown: BuildScopedTriageMarkdown(payload),
                systemStateContext: null,
                resourceUri: BuildUiResourceUri("triage", "sarif_triage", string.Empty),
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

        private static async Task<ScopedGetPayload> ExecuteScopedGetAsync(string scope, string filter, bool includeEvidence, int limit, int page = 0, bool debugPrompt = false, string pageToken = "")
        {
            var scopeAction = ParseScopeAction(scope);
            var parsedFilter = string.IsNullOrWhiteSpace(filter)
                ? new ActiveScopeFilter()
                : ParseScopeFilter(filter);
            if (page < 0)
            {
                throw new ArgumentException("page must be greater than or equal to 0.", nameof(page));
            }

            var parsedPageTokenOffset = ParsePageTokenOffset(pageToken);
            var hasExplicitPageToken = !string.IsNullOrWhiteSpace(pageToken);
            var hasExplicitPage = page > 0;

            if (scopeAction == ScopeAction.Refine && parsedFilter.IsEmpty)
            {
                throw new ArgumentException("filter is required when scope is refine.", nameof(filter));
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
            var paginationScopeKey = BuildPaginationScopeKey(executionScope);
            var batchLimit = limit <= 0 ? 10 : limit;
            var resetToFirstPage = scopeAction != ScopeAction.Keep;

            var cursorOffset = parsedPageTokenOffset;
            if (hasExplicitPage)
            {
                cursorOffset = (page - 1) * batchLimit;
            }
            else if (!hasExplicitPageToken)
            {
                lock (SyncRoot)
                {
                    if (resetToFirstPage)
                    {
                        _paginationScopeKey = paginationScopeKey;
                        _paginationNextOffset = 0;
                        cursorOffset = 0;
                    }
                    else if (string.Equals(_paginationScopeKey, paginationScopeKey, StringComparison.Ordinal))
                    {
                        cursorOffset = _paginationNextOffset;
                    }
                    else
                    {
                        _paginationScopeKey = paginationScopeKey;
                        _paginationNextOffset = 0;
                        cursorOffset = 0;
                    }
                }
            }

            var workflow = CreateTriageWorkflowService();

            var activeScopeFindings = await workflow.ListAsync(activeScope.ToQueryOptions(int.MaxValue));
            var executionScopeFindings = executionScope == activeScope
                ? activeScopeFindings
                : await workflow.ListAsync(executionScope.ToQueryOptions(int.MaxValue));

            var effectiveOffset = Math.Min(cursorOffset, executionScopeFindings.Count);
            var executionFindings = executionScopeFindings
                .Skip(effectiveOffset)
                .Take(batchLimit)
                .ToList();
            var nextOffset = effectiveOffset + executionFindings.Count;
            var hasMore = nextOffset < executionScopeFindings.Count;
            var totalPages = executionScopeFindings.Count <= 0
                ? 1
                : (int)Math.Ceiling((double)executionScopeFindings.Count / batchLimit);
            var currentPage = (int)Math.Floor((double)effectiveOffset / batchLimit) + 1;
            var previousPageToken = currentPage > 1
                ? ((currentPage - 2) * batchLimit).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            var previousPageNumber = currentPage > 1 ? currentPage - 1 : (int?)null;
            var nextPageNumber = hasMore ? currentPage + 1 : (int?)null;

            lock (SyncRoot)
            {
                _paginationScopeKey = paginationScopeKey;
                _paginationNextOffset = hasMore ? nextOffset : effectiveOffset;
            }

            IReadOnlyDictionary<string, TriageInspectResult>? evidenceByFindingId = null;
            if (includeEvidence && executionFindings.Count > 0)
            {
                var evidenceTargetIds = executionFindings
                    .Select(item => item.FindingId)
                    .Take(MaxEvidenceInspectCount)
                    .ToList();

                evidenceByFindingId = await workflow.InspectManyAsync(evidenceTargetIds);
            }

            var promptAssembly = PromptAssembly;
            var findingRows = new List<ScopedFinding>(executionFindings.Count);
            foreach (var finding in executionFindings)
            {
                var displayId = GetOrCreateDisplayId(finding.FindingId);
                TriageInspectResult? evidence = null;
                if (includeEvidence && evidenceByFindingId != null)
                {
                    evidenceByFindingId.TryGetValue(finding.FindingId, out evidence);
                }

                string? triagePrompt = null;
                if (debugPrompt && promptAssembly != null)
                {
                    triagePrompt = await promptAssembly.BuildTriagePromptAsync(
                        evidence?.RuleId ?? finding.RuleName,
                        evidence?.Message ?? finding.RuleName).ConfigureAwait(false);
                }

                findingRows.Add(new ScopedFinding(
                    displayId,
                    finding.FindingId,
                    finding.Severity,
                    finding.State,
                    finding.RuleName,
                    evidence?.Message ?? finding.RuleName,
                    new ScopedLocation(finding.FilePath, finding.LineNumber),
                    evidence,
                    triagePrompt));
            }

            var metrics = new SarifGetMetrics(
                activeScopeFindings.Count,
                findingRows.Count,
                activeScopeFindings.Count(item => string.Equals(item.State, TriageFindingState.Open.ToString(), StringComparison.OrdinalIgnoreCase)));

            var pagination = new ScopedPagination(
                effectiveOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                batchLimit,
                hasMore
                    ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty,
                hasMore,
                currentPage,
                totalPages,
                previousPageToken,
                nextPageNumber,
                previousPageNumber);

            return new ScopedGetPayload(
                new ScopedContext(
                    "Results are filtered by the persistent Active Scope.",
                    ToScopeDictionary(activeScope),
                    new ScopedMetrics(metrics.TotalInScope, metrics.ReturnedInBatch, metrics.RemainingInScope),
                    pagination),
                findingRows,
                debugPrompt);
        }

        /// <summary>
        /// Resets displayId-to-findingId mappings when scope changes invalidate prior aliases.
        /// </summary>
        private static void ResetDisplayIdMappings()
        {
            lock (SyncRoot)
            {
                DisplayIdToFindingId.Clear();
                FindingIdToDisplayId.Clear();
                _nextDisplayId = 1;
            }
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

            var (requestedState, workflowState) = NormalizeDecisionState(state);
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
                targetIds = ResolveFindingIds(target)
                    .Select(ResolveFindingIdFromAliasOrRaw)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            var modifiedIds = new List<string>();
            foreach (var targetId in targetIds)
            {
                var decision = await workflow.TriageAsync(targetId, workflowState, reason, author);
                if (decision.Success)
                {
                    modifiedIds.Add(targetId);
                }
            }

            var evidenceRows = new List<ScopedTriageEvidence>(modifiedIds.Count);
            if (modifiedIds.Count > 0)
            {
                var inspectionTargets = modifiedIds
                    .Take(MaxEvidenceInspectCount)
                    .ToList();

                var evidenceByFindingId = await workflow.InspectManyAsync(inspectionTargets);
                foreach (var findingId in inspectionTargets)
                {
                    evidenceByFindingId.TryGetValue(findingId, out var evidence);

                    string displayId;
                    lock (SyncRoot)
                    {
                        displayId = FindingIdToDisplayId.TryGetValue(findingId, out var did)
                            ? did
                            : findingId;
                    }

                    evidenceRows.Add(new ScopedTriageEvidence(
                        findingId,
                        displayId,
                        evidence));
                }
            }

            return new ScopedTriagePayload(
                modifiedIds.Count == targetIds.Count,
                requestedState,
                reason,
                target,
                modifiedIds.Count,
                modifiedIds,
                workflowState,
                evidenceRows);
        }

        private static (string RequestedState, string WorkflowState) NormalizeDecisionState(string state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                throw new ArgumentException("state is required.", nameof(state));
            }

            var normalized = state.Trim().ToLowerInvariant();
            return normalized switch
            {
                "confirmed" => ("confirmed", "TP"),
                "false_positive" => ("false_positive", "FP"),
                "test_code" => ("test_code", "FP"),
                "wont_fix" => ("wont_fix", "TP"),
                "mitigated" => ("mitigated", "TP"),
                _ => throw new ArgumentException("state must be one of: confirmed, false_positive, test_code, wont_fix, mitigated.", nameof(state))
            };
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

        private static JsonObject BuildScopedMeta(ScopedGetPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            return JsonSerializer.SerializeToNode(new
            {
                context = new
                {
                    notice = payload.Context.Notice,
                    active_scope = payload.Context.ActiveScope,
                    metrics = new
                    {
                        total_in_scope = payload.Context.Metrics.TotalInScope,
                        returned_in_batch = payload.Context.Metrics.ReturnedInBatch,
                        remaining_in_scope = payload.Context.Metrics.RemainingInScope
                    },
                    pagination = new
                    {
                        page_token = payload.Context.Pagination.PageToken,
                        page_size = payload.Context.Pagination.PageSize,
                        page_number = payload.Context.Pagination.PageNumber,
                        total_pages = payload.Context.Pagination.TotalPages,
                        has_more = payload.Context.Pagination.HasMore,
                        next_page_token = payload.Context.Pagination.NextPageToken,
                        previous_page_token = payload.Context.Pagination.PreviousPageToken,
                        next_page_number = payload.Context.Pagination.NextPageNumber,
                        previous_page_number = payload.Context.Pagination.PreviousPageNumber
                    },
                    aliases = payload.Findings
                        .Select(item => new { displayid = item.DisplayId, finding_id = item.FindingId })
                        .ToArray()
                }
            }) as JsonObject ?? new JsonObject();
        }

        private static int ParsePageTokenOffset(string pageToken)
        {
            if (string.IsNullOrWhiteSpace(pageToken))
            {
                return 0;
            }

            if (!int.TryParse(pageToken.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset) || offset < 0)
            {
                throw new ArgumentException("pageToken must be a non-negative integer.", nameof(pageToken));
            }

            return offset;
        }

        private static string BuildPaginationScopeKey(ActiveScopeFilter scope)
        {
            ArgumentNullException.ThrowIfNull(scope);

            return string.Join('|',
                scope.Severity.Trim(),
                scope.Rule.Trim(),
                scope.File.Trim(),
                scope.State.Trim()).ToLowerInvariant();
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
            var reportMarkdown = markdown?.Trim() ?? string.Empty;
            var text = $$"""
            <system_directive>
            CRITICAL OUTPUT CONTRACT:
            1) Output the content inside the <vulnerability_report> tags VERBATIM.
            2) Output exactly one <vulnerability_report> block (no duplicates).
            3) Do not summarize, restate, interpret, or add any additional tables or prose.
            4) Do not ask follow-up questions.
            5) Stop immediately after the single report block.
            </system_directive>

            <vulnerability_report>
            {{reportMarkdown}}
            </vulnerability_report>
            """;

            if (systemStateContext != null)
            {
                var contextJson = JsonSerializer.Serialize(systemStateContext);
                text = $$"""
                {{text}}

                {{StateContextDelimiter}}
                {{contextJson}}
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
            var currentPage = payload.Context.Pagination.PageNumber;
            var totalPages = payload.Context.Pagination.TotalPages;
            var nextPage = payload.Context.Pagination.NextPageNumber ?? currentPage;

            var lines = new List<string>
            {
                "## SARIF Scoped Query",
                string.Empty,
                $"- Total in scope: **{metrics.TotalInScope}**",
                $"- Returned in batch: **{metrics.ReturnedInBatch}**",
                $"- Remaining in scope: **{metrics.RemainingInScope}**",
                $"- Page: **{currentPage} of {totalPages}**",
                $"- Page size: **{payload.Context.Pagination.PageSize}**",
                $"- Has more: **{payload.Context.Pagination.HasMore}**",
                string.Empty
            };

            if (payload.Context.Pagination.HasMore)
            {
                lines.Add($"- Next page: **{nextPage} of {totalPages}**");
                lines.Add(string.Empty);
            }

            if (payload.Context.Pagination.PreviousPageNumber.HasValue)
            {
                lines.Add($"- Previous page: **{payload.Context.Pagination.PreviousPageNumber.Value} of {totalPages}**");
                lines.Add(string.Empty);
            }

            if (findings.Count == 0)
            {
                lines.Add("No findings in current result set.");
            }
            else
            {
                lines.Add("| Id | Severity | State | Rule | Location |\n|---|---|---|---|---|");
                foreach (var finding in findings)
                {
                    lines.Add($"| `{EscapeMarkdown(finding.DisplayId)}` | `{EscapeMarkdown(finding.Severity)}` | `{EscapeMarkdown(finding.State)}` | `{EscapeMarkdown(finding.Rule)}` | `{EscapeMarkdown(finding.Location.File)}`:{finding.Location.Line?.ToString() ?? "?"} |");
                }

            }

            lines.Add(string.Empty);
            lines.Add("**STOP:** Wait for explicit user instruction.");
            lines.Add("If the user asks to triage, call `sarif_triage` with `state`, `reason`, and `target` (displayid).");
            if (payload.Context.Pagination.HasMore)
            {
                lines.Add($"Optional fetch: if the user explicitly asks for more findings, call `sarif_get` once with `page: {nextPage}` or with `context.pagination.next_page_token`.");
                lines.Add("Do not auto-fetch another batch; wait for the user instruction.");
            }

            if (payload.Context.Pagination.PreviousPageNumber.HasValue)
            {
                lines.Add($"To go back, call `sarif_get` once with `page: {payload.Context.Pagination.PreviousPageNumber.Value}`.");
            }

            if (payload.DebugPrompt)
            {
                var debugFindings = findings.Where(f => !string.IsNullOrWhiteSpace(f.TriagePrompt)).ToList();
                if (debugFindings.Count > 0)
                {
                    lines.Add(string.Empty);
                    lines.Add("---");
                    lines.Add("### DEBUG: Assembled Triage Prompts");
                    lines.Add(string.Empty);
                    foreach (var finding in debugFindings)
                    {
                        lines.Add($"<details><summary>Prompt for finding {EscapeMarkdown(finding.DisplayId)} ({EscapeMarkdown(finding.Rule)})</summary>");
                        lines.Add(string.Empty);
                        lines.Add("```");
                        lines.Add(finding.TriagePrompt!);
                        lines.Add("```");
                        lines.Add(string.Empty);
                        lines.Add("</details>");
                        lines.Add(string.Empty);
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildScopedTriageMarkdown(ScopedTriagePayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            var lines = new List<string>
            {
                "## SARIF Scoped Triage",
                string.Empty,
                $"- Success: **{payload.Success}**",
                $"- State: `{EscapeMarkdown(payload.State)}`",
                $"- Internal workflow state: `{EscapeMarkdown(payload.WorkflowState)}`",
                $"- Target: `{EscapeMarkdown(payload.Target)}`",
                $"- Affected findings: **{payload.AffectedCount}**",
                $"- Original reasoning: {EscapeMarkdown(payload.Reason)}"
            };

            if (payload.ModifiedFindingIds.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("### Modified Findings");
                foreach (var findingId in payload.ModifiedFindingIds)
                {
                    string displayLabel;
                    lock (SyncRoot)
                    {
                        displayLabel = FindingIdToDisplayId.TryGetValue(findingId, out var did)
                            ? $"`{did}`"
                            : $"`{findingId}`";
                    }

                    lines.Add($"- {displayLabel} → `{EscapeMarkdown(payload.WorkflowState)}`");
                }
            }

            if (payload.Evidence.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("### Decision Evidence");

                foreach (var evidenceRow in payload.Evidence)
                {
                    lines.Add(string.Empty);
                    lines.Add($"#### Finding `{EscapeMarkdown(evidenceRow.DisplayId)}`");

                    if (evidenceRow.Evidence != null)
                    {
                        lines.Add($"- Rule: `{EscapeMarkdown(evidenceRow.Evidence.RuleId)}`");
                        lines.Add($"- Severity: `{EscapeMarkdown(evidenceRow.Evidence.Severity)}`");
                        lines.Add($"- Message: {EscapeMarkdown(evidenceRow.Evidence.Message)}");

                        lines.Add(string.Empty);
                        lines.Add("##### Data Flow Used");
                        if (evidenceRow.Evidence.DataFlowEvidenceBlocks.Count > 0)
                        {
                            foreach (var block in evidenceRow.Evidence.DataFlowEvidenceBlocks)
                            {
                                lines.Add($"- Steps `{block.StartStepIndex}`-`{block.EndStepIndex}` at `{EscapeMarkdown(block.FilePath)}`:{block.StartLine?.ToString() ?? "?"}-{block.EndLine?.ToString() ?? "?"}");
                                lines.Add("```csharp");
                                lines.Add(block.CodeSnippet);
                                lines.Add("```");
                            }
                        }
                        else if (evidenceRow.Evidence.DataFlowSteps.Count > 0)
                        {
                            foreach (var step in evidenceRow.Evidence.DataFlowSteps)
                            {
                                lines.Add($"- Step `{step.Index}` at `{EscapeMarkdown(step.FilePath)}`:{step.StartLine?.ToString() ?? "?"} — {EscapeMarkdown(step.Message)}");
                            }
                        }
                        else
                        {
                            lines.Add("- No data flow blocks were available for this finding.");
                        }
                    }
                    else
                    {
                        lines.Add("- Evidence unavailable for this finding.");
                    }

                }
            }

            lines.Add(string.Empty);
            lines.Add("**Next action:** Run `sarif_get` to verify remaining findings in scope.");

            return string.Join(Environment.NewLine, lines);
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

        private static string GetOrCreateDisplayId(string findingId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(findingId);

            lock (SyncRoot)
            {
                if (FindingIdToDisplayId.TryGetValue(findingId, out var existingDisplayId))
                {
                    return existingDisplayId;
                }

                var displayId = _nextDisplayId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _nextDisplayId++;

                FindingIdToDisplayId[findingId] = displayId;
                DisplayIdToFindingId[displayId] = findingId;
                DisplayIdToFindingId[$"@{displayId}"] = findingId;
                DisplayIdToFindingId[$"S-{int.Parse(displayId, System.Globalization.CultureInfo.InvariantCulture):00}"] = findingId;

                return displayId;
            }
        }

        private static string ResolveFindingIdFromAliasOrRaw(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var normalized = token.Trim();
            lock (SyncRoot)
            {
                if (DisplayIdToFindingId.TryGetValue(normalized, out var aliasedFindingId))
                {
                    return aliasedFindingId;
                }
            }

            return normalized;
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

        private sealed record ScopedPagination(
            string PageToken,
            int PageSize,
            string NextPageToken,
            bool HasMore,
            int PageNumber,
            int TotalPages,
            string PreviousPageToken,
            int? NextPageNumber,
            int? PreviousPageNumber);

        private sealed record ScopedContext(
            string Notice,
            IReadOnlyDictionary<string, string> ActiveScope,
            ScopedMetrics Metrics,
            ScopedPagination Pagination);

        private sealed record ScopedLocation(string File, int? Line);

        private sealed record ScopedFinding(
            string DisplayId,
            string FindingId,
            string Severity,
            string State,
            string Rule,
            string Message,
            ScopedLocation Location,
            TriageInspectResult? Evidence,
            string? TriagePrompt = null);

        private sealed record ScopedGetPayload(ScopedContext Context, IReadOnlyList<ScopedFinding> Findings, bool DebugPrompt = false);

        private sealed record ScopedTriagePayload(
            bool Success,
            string State,
            string Reason,
            string Target,
            int AffectedCount,
            IReadOnlyList<string> ModifiedFindingIds,
            string WorkflowState,
            IReadOnlyList<ScopedTriageEvidence> Evidence);

        private sealed record ScopedTriageEvidence(
            string FindingId,
            string DisplayId,
            TriageInspectResult? Evidence);

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
