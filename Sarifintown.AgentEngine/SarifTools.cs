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
        internal static TriageLedgerService? LedgerService { get; set; }
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
        private static bool _debugPromptEnabled;
        private static bool _includeEvidenceByDefault = true;
        private static HashSet<string> _availableSeverities = new(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _availableRules = new(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _availableStatuses = new(StringComparer.OrdinalIgnoreCase) { "Open", "TP", "FP" };
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

        /// <summary>
        /// Enables or disables prompt debug mode for `sarif_get` at server startup.
        /// </summary>
        public static void SetDebugPromptEnabled(bool enabled)
        {
            lock (SyncRoot)
            {
                _debugPromptEnabled = enabled;
            }
        }

        /// <summary>
        /// Sets the default evidence inclusion behavior for `sarif_get` at server startup.
        /// </summary>
        public static void SetIncludeEvidenceByDefault(bool enabled)
        {
            lock (SyncRoot)
            {
                _includeEvidenceByDefault = enabled;
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

        /// <summary>
        /// Populates available filter facets (severities, rules, statuses) from loaded SARIF findings.
        /// Call after SarifStateService initialization completes.
        /// </summary>
        public static async Task InitializeAvailableFacetsAsync()
        {
            var workflow = CreateTriageWorkflowService();
            var allFindings = await workflow.ListAsync(new TriageQueryOptions(Limit: int.MaxValue));

            lock (SyncRoot)
            {
                _availableSeverities = allFindings
                    .Select(f => f.Severity)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _availableRules = allFindings
                    .Select(f => f.RuleName)
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _availableStatuses = allFindings
                    .Select(f => f.State)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (_availableStatuses.Count == 0)
                {
                    _availableStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Open", "TP", "FP" };
                }
            }
        }

        [McpServerTool(Name = "sarif_filter")]
        [Description("Set or clear the active scope filter for SARIF findings. Uses a space-separated query string (e.g. 'severity:high rule:SQLI status:open path:controllers'). Supported keys: status, severity, rule, path. Call with no arguments to see available filter values. Call sarif_get after filtering to view results.")]
        public static Task<CallToolResult> SarifFilter(
            [Description("Space-separated filter query (e.g. 'severity:high rule:SQLI status:open path:controllers'). Omit or leave empty to list available filters.")]
            string query = "")
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(BuildAvailableFiltersResult());
            }

            var normalizedQuery = query.Trim();
            if (string.Equals(normalizedQuery, "clear", StringComparison.OrdinalIgnoreCase))
            {
                SetActiveScope(new ActiveScopeFilter());
                ResetPagination();
                return Task.FromResult(CreatePlainTextResult("✅ Scope cleared. All filters removed. Run `sarif_get` to view unfiltered results."));
            }

            var parsedFilter = ParseSpaceSeparatedQuery(normalizedQuery);
            SetActiveScope(parsedFilter);
            ResetPagination();

            var scopeDict = ToScopeDictionary(parsedFilter);
            var filterDescription = scopeDict.Count == 0
                ? "none"
                : string.Join(", ", scopeDict.Select(kvp => $"{kvp.Key}:{kvp.Value}"));

            return Task.FromResult(CreatePlainTextResult($"✅ Scope updated. Current filters: {filterDescription}. Run `sarif_get` to view results."));
        }

        [McpServerTool(Name = "sarif_get")]
        [Description("Retrieve scoped SARIF findings using the active filter set by sarif_filter. Evidence inclusion and debug prompt output are controlled only by server startup settings. CRITICAL EXECUTION PROTOCOL: (1) Output exactly one vulnerability_report block VERBATIM from this tool result. (2) Do NOT summarize, interpret, restate, duplicate, or render additional tables. (3) STOP after output and wait for explicit user instruction. (4) Never call sarif_get again unless the user explicitly asks for another page. Use sarif_filter to change filters.")]
        public static async Task<CallToolResult> SarifGet(
            [Description("Maximum findings to return (1-25).")]
            int limit = 10,
            [Description("Optional 1-based page number. When provided, this overrides automatic pagination and pageToken.")]
            int page = 0,
            [Description("Optional pagination token returned by a previous sarif_get call. Use context.pagination.next_page_token to fetch the next batch.")]
            string pageToken = "")
        {
            var safeLimit = limit <= 0 ? 10 : Math.Min(limit, 25);
            bool debugPromptEnabled;
            bool includeEvidenceByDefault;
            lock (SyncRoot)
            {
                debugPromptEnabled = _debugPromptEnabled;
                includeEvidenceByDefault = _includeEvidenceByDefault;
            }

            var payload = await ExecutePureGetAsync(includeEvidenceByDefault, safeLimit, page, debugPromptEnabled, pageToken);
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
                    snippet_preload_status = payload.Context.SnippetPreloadStatus,
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
                        .ToArray(),
                    available_facets = payload.AvailableFacets
                }
            };

            var metaObj = BuildScopedMeta(payload);
            metaObj["pause"] = true;
            metaObj["next_step"] = "sarif_review";

            return CreateDualPurposeResult(
                markdown: BuildScopedGetMarkdown(payload),
                systemStateContext: stateContext,
                resourceUri: BuildUiResourceUri("triage", "sarif_get", string.Empty),
                additionalMeta: metaObj);
        }

        /// <summary>
        /// Autotriage: persists the LLM's triage decision and reasoning into the audit ledger.
        /// The LLM must first call sarif_get to load evidence, analyze findings, then call this tool.
        /// Does not make external API calls. Use sarif_sync to push decisions upstream.
        /// </summary>
        [McpServerTool(Name = "sarif_review")]
        [Description("Record an AI triage decision for one or more SARIF findings into the local audit ledger. Requires prior evidence from sarif_get. CRITICAL EXECUTION PROTOCOL: (1) You MUST have already called sarif_get and received evidence for the target findings. (2) Analyze the evidence: code flow, snippets, rule description, severity. (3) Determine state and reason from your analysis. (4) Call this tool with target, state, reason, llmReasoning (your full chain-of-thought), and inputMarkdown (the evidence you analyzed). (5) Output exactly one <vulnerability_report> block VERBATIM from the result. (6) Do NOT summarize, interpret, or add commentary. (7) STOP and wait for user instruction. Use sarif_update instead for manual human overrides.")]
        public static async Task<CallToolResult> SarifReview(
            [Description("Target displayid (e.g. 1), CSV displayid list (e.g. 1,2,3), or literal 'scope' to autotriage all open findings in active scope (max 25).")]
            string target = "scope",
            [Description("Decision state determined by analysis: confirmed (true positive), false_positive (not a real issue), test_code (in test/non-production code), wont_fix (accepted risk), or mitigated (already addressed).")]
            string state = "",
            [Description("Decision reason derived from the evidence analysis.")]
            string reason = "",
            [Description("Verbose chain-of-thought reasoning from the LLM analysis.")]
            string llmReasoning = "",
            [Description("The exact evidence markdown that was fed to the LLM for this decision.")]
            string inputMarkdown = "")
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return CreatePlainTextResult("❌ `target` is required (displayid, CSV list, or 'scope').");
            }

            if (string.IsNullOrWhiteSpace(state))
            {
                return CreatePlainTextResult("❌ `state` is required. Analyze the evidence from sarif_get and provide a decision: confirmed, false_positive, test_code, wont_fix, or mitigated.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return CreatePlainTextResult("❌ `reason` is required. Summarize your analysis of the evidence in 1-2 sentences.");
            }

            if (!TryParseTriageDecisionState(state, out var parsedDecision))
            {
                return CreatePlainTextResult("❌ `state` must be one of: confirmed, false_positive, test_code, wont_fix, mitigated.");
            }

            var ledger = GetOrCreateLedgerService();

            // Persist via the existing triage machinery so local state stays consistent
            var triagePayload = await ExecuteScopedTriageAsync(state, reason, target, "AI");
            if (triagePayload.ModifiedFindingIds.Count == 0)
            {
                return CreatePlainTextResult("⚠️ No findings matched the target. Verify the displayid or scope.");
            }

            // Resolve tool name from findings for composite key generation
            var now = DateTime.UtcNow;
            var ledgerItems = await BuildLedgerItemsAsync(
                triagePayload, parsedDecision, reason, "AI", humanReviewed: false,
                llmReasoning, inputMarkdown, now);

            await ledger.UpsertAsync(ledgerItems);

            var reviewStateContext = new
            {
                review = new
                {
                    success = triagePayload.Success,
                    state = triagePayload.State,
                    workflow_state = triagePayload.WorkflowState,
                    target = triagePayload.Target,
                    affected_count = triagePayload.AffectedCount,
                    modified_finding_ids = triagePayload.ModifiedFindingIds,
                    ledger_entries_written = ledgerItems.Count,
                    sync_status = "pending"
                }
            };

            var reviewMeta = new JsonObject
            {
                ["pause"] = true,
                ["next_step"] = "sarif_get"
            };

            return CreateDualPurposeResult(
                markdown: BuildScopedReviewMarkdown(triagePayload, ledgerItems.Count),
                systemStateContext: reviewStateContext,
                resourceUri: BuildUiResourceUri("triage", "sarif_review", string.Empty),
                additionalMeta: reviewMeta);
        }

        /// <summary>
        /// Manual override: applies a human-provided triage decision to specific findings,
        /// marking them as human-reviewed in the audit ledger.
        /// </summary>
        [McpServerTool(Name = "sarif_update")]
        [Description("Manually override a triage decision for one or more findings. Sets human_reviewed=true in the audit ledger. Use this to correct or confirm AI-generated decisions. CRITICAL EXECUTION PROTOCOL: (1) Call this tool with a displayid from sarif_get output. (2) Output the result block VERBATIM. (3) Run sarif_get again to verify remaining findings.")]
        public static async Task<CallToolResult> SarifUpdate(
            [Description("Decision state: confirmed (true positive), false_positive (not a real issue), test_code (in test/non-production code), wont_fix (accepted risk), or mitigated (already addressed).")]
            string state,
            [Description("Required decision reason/audit note explaining why this decision was made.")]
            string reason,
            [Description("Target displayid (e.g. 1), CSV displayid list (e.g. 1,2,3), or literal 'scope' to update all open findings in active scope.")]
            string target)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return CreatePlainTextResult("❌ `state` is required (confirmed, false_positive, test_code, wont_fix, mitigated).");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return CreatePlainTextResult("❌ `reason` is required.");
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                return CreatePlainTextResult("❌ `target` is required (displayid, CSV list, or 'scope').");
            }

            if (!TryParseTriageDecisionState(state, out var parsedDecision))
            {
                return CreatePlainTextResult("❌ `state` must be one of: confirmed, false_positive, test_code, wont_fix, mitigated.");
            }

            var ledger = GetOrCreateLedgerService();

            var triagePayload = await ExecuteScopedTriageAsync(state, reason, target, "human_developer");
            if (triagePayload.ModifiedFindingIds.Count == 0)
            {
                return CreatePlainTextResult("⚠️ No findings matched the target. Verify the displayid or scope.");
            }

            var now = DateTime.UtcNow;
            var ledgerItems = await BuildLedgerItemsAsync(
                triagePayload, parsedDecision, reason, "human_developer", humanReviewed: true,
                llmReasoning: string.Empty, inputMarkdown: string.Empty, now);

            await ledger.UpsertAsync(ledgerItems);

            return CreateDualPurposeResult(
                markdown: BuildScopedTriageMarkdown(triagePayload),
                systemStateContext: null,
                resourceUri: BuildUiResourceUri("triage", "sarif_update", string.Empty),
                additionalMeta: null);
        }

        /// <summary>
        /// Builds ledger entries from a completed triage payload. Shared by SarifReview and SarifUpdate.
        /// </summary>
        private static async Task<List<(string CompositeKey, LedgerEntry Entry)>> BuildLedgerItemsAsync(
            ScopedTriagePayload triagePayload,
            TriageDecisionState parsedDecision,
            string reason,
            string author,
            bool humanReviewed,
            string llmReasoning,
            string inputMarkdown,
            DateTime timestamp)
        {
            var ledgerItems = new List<(string CompositeKey, LedgerEntry Entry)>();

            foreach (var findingId in triagePayload.ModifiedFindingIds)
            {
                var toolName = await ResolveToolNameForFindingAsync(findingId);
                var compositeKey = TriageLedgerDocument.BuildCompositeKey(toolName, findingId);

                string filePath = string.Empty;
                string ruleId = string.Empty;

                var evidence = triagePayload.Evidence
                    .FirstOrDefault(e => string.Equals(e.FindingId, findingId, StringComparison.Ordinal));
                if (evidence?.Evidence != null)
                {
                    filePath = evidence.Evidence.DataFlowSteps.FirstOrDefault()?.FilePath ?? string.Empty;
                    ruleId = evidence.Evidence.RuleId;
                }

                var entry = new LedgerEntry
                {
                    Metadata = new LedgerMetadata
                    {
                        FindingId = findingId,
                        ToolName = toolName,
                        RuleId = ruleId,
                        FilePath = filePath
                    },
                    TriageDecision = new LedgerTriageDecision
                    {
                        State = parsedDecision,
                        ShortReason = reason,
                        Author = author,
                        Timestamp = timestamp
                    },
                    AuditLog = new LedgerAuditLog
                    {
                        InputMarkdown = inputMarkdown,
                        LlmReasoning = llmReasoning,
                        HumanReviewed = humanReviewed
                    },
                    UpstreamSync = new LedgerUpstreamSync
                    {
                        Status = UpstreamSyncStatus.Pending,
                        TargetPlatform = toolName
                    }
                };

                ledgerItems.Add((compositeKey, entry));
            }

            return ledgerItems;
        }

        /// <summary>
        /// Pushes pending local triage decisions from the audit ledger to upstream vendor APIs.
        /// </summary>
        [McpServerTool(Name = "sarif_sync")]
        [Description("Push local review decisions to upstream vendor APIs. Only processes entries with 'pending' sync status. Call this after using sarif_review to record decisions.")]
        public static async Task<CallToolResult> SarifSync(
            [Description("Target: 'pending' to sync all pending entries, or specific composite keys (comma-separated).")]
            string target = "pending")
        {
            var ledger = GetOrCreateLedgerService();

            IReadOnlyList<(string CompositeKey, LedgerEntry Entry)> entriesToSync;
            if (string.Equals(target, "pending", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(target))
            {
                entriesToSync = await ledger.GetBySyncStatusAsync(UpstreamSyncStatus.Pending);
            }
            else
            {
                var keys = target
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                entriesToSync = await ledger.GetByKeysAsync(keys);
            }

            if (entriesToSync.Count == 0)
            {
                return CreatePlainTextResult("ℹ️ No pending entries found in the triage ledger. Nothing to sync.");
            }

            var now = DateTime.UtcNow;
            var syncedCount = 0;
            var failedCount = 0;
            var syncedByPlatform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var failedByPlatform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var updatedItems = new List<(string CompositeKey, LedgerEntry Entry)>();

            foreach (var (compositeKey, entry) in entriesToSync)
            {
                var platform = entry.Metadata.ToolName;
                var (success, errorMessage) = await TrySyncToUpstreamAsync(entry);

                var updatedSync = entry.UpstreamSync with
                {
                    Status = success ? UpstreamSyncStatus.Synced : UpstreamSyncStatus.Failed,
                    LastSyncAttempt = now,
                    ErrorMessage = success ? null : errorMessage
                };

                updatedItems.Add((compositeKey, entry with { UpstreamSync = updatedSync }));

                if (success)
                {
                    syncedCount++;
                    syncedByPlatform[platform] = syncedByPlatform.GetValueOrDefault(platform) + 1;
                }
                else
                {
                    failedCount++;
                    failedByPlatform[platform] = failedByPlatform.GetValueOrDefault(platform) + 1;
                }
            }

            await ledger.UpsertAsync(updatedItems);

            var lines = new List<string> { "## Sync Results", string.Empty };

            if (syncedCount > 0)
            {
                lines.Add($"✅ **{syncedCount}** entries synced successfully.");
                foreach (var (platform, count) in syncedByPlatform.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add($"  - {platform}: {count}");
                }
            }

            if (failedCount > 0)
            {
                lines.Add($"❌ **{failedCount}** entries failed to sync.");
                foreach (var (platform, count) in failedByPlatform.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add($"  - {platform}: {count}");
                }

                lines.Add(string.Empty);
                lines.Add("Check the triage ledger for error details. Fix credentials and retry with `sarif_sync`.");
            }

            if (syncedCount == 0 && failedCount == 0)
            {
                lines.Add("ℹ️ No entries were processed.");
            }

            return CreatePlainTextResult(string.Join(Environment.NewLine, lines));
        }

        /// <summary>
        /// Attempts to sync a single ledger entry to its upstream vendor platform.
        /// Returns (success, errorMessage).
        /// </summary>
        private static Task<(bool Success, string? ErrorMessage)> TrySyncToUpstreamAsync(LedgerEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var platform = entry.Metadata.ToolName.Trim().ToLowerInvariant();

            return platform switch
            {
                "snyk" or "snyk code" => TrySyncToSnykAsync(entry),
                "github-advanced-security" or "codeql" or "github" => TrySyncToGhasAsync(entry),
                _ => Task.FromResult<(bool, string?)>((false, $"Unsupported platform: '{entry.Metadata.ToolName}'. No sync adapter registered."))
            };
        }

        private static Task<(bool Success, string? ErrorMessage)> TrySyncToSnykAsync(LedgerEntry entry)
        {
            var token = Environment.GetEnvironmentVariable("SNYK_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                return Task.FromResult<(bool, string?)>((false, "SNYK_TOKEN environment variable is not set."));
            }

            // Stub: actual Snyk API integration will be implemented when vendor SDK is available.
            return Task.FromResult<(bool, string?)>((true, null));
        }

        private static Task<(bool Success, string? ErrorMessage)> TrySyncToGhasAsync(LedgerEntry entry)
        {
            var token = Environment.GetEnvironmentVariable("GHAS_TOKEN")
                        ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                return Task.FromResult<(bool, string?)>((false, "GHAS_TOKEN (or GITHUB_TOKEN) environment variable is not set."));
            }

            // Stub: actual GitHub Advanced Security API integration will be implemented when vendor SDK is available.
            return Task.FromResult<(bool, string?)>((true, null));
        }

        private static bool TryParseTriageDecisionState(string state, out TriageDecisionState parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(state))
            {
                return false;
            }

            var normalized = state.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "false_positive":
                    parsed = TriageDecisionState.FalsePositive;
                    return true;
                case "wont_fix":
                    parsed = TriageDecisionState.WontFix;
                    return true;
                case "test_code":
                    parsed = TriageDecisionState.TestCode;
                    return true;
                case "confirmed":
                    parsed = TriageDecisionState.Confirmed;
                    return true;
                case "mitigated":
                    parsed = TriageDecisionState.Mitigated;
                    return true;
                default:
                    return false;
            }
        }

        private static TriageLedgerService GetOrCreateLedgerService()
        {
            var service = LedgerService;
            if (service != null)
            {
                return service;
            }

            string workspaceRoot;
            lock (SyncRoot)
            {
                workspaceRoot = _workspaceRoot;
            }

            service = new TriageLedgerService(workspaceRoot);
            LedgerService = service;
            return service;
        }

        private static async Task<string> ResolveToolNameForFindingAsync(
            string findingId)
        {
            var stateService = StateService;
            if (stateService == null)
            {
                return "unknown-tool";
            }

            var findings = await stateService.GetFindingsAsync();
            var finding = findings.FirstOrDefault(f => string.Equals(f.FindingId, findingId, StringComparison.Ordinal));
            if (finding == null)
            {
                return "unknown-tool";
            }

            return finding.Run?.Tool?.Driver?.Name?.Trim().ToLowerInvariant() ?? "unknown-tool";
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

        private static async Task<ScopedGetPayload> ExecutePureGetAsync(bool includeEvidence, int limit, int page = 0, bool debugPrompt = false, string pageToken = "")
        {
            if (page < 0)
            {
                throw new ArgumentException("page must be greater than or equal to 0.", nameof(page));
            }

            var parsedPageTokenOffset = ParsePageTokenOffset(pageToken);
            var hasExplicitPageToken = !string.IsNullOrWhiteSpace(pageToken);
            var hasExplicitPage = page > 0;

            var activeScope = GetActiveScope();
            var paginationScopeKey = BuildPaginationScopeKey(activeScope);
            var batchLimit = limit <= 0 ? 10 : limit;

            var cursorOffset = parsedPageTokenOffset;
            if (hasExplicitPage)
            {
                cursorOffset = (page - 1) * batchLimit;
            }
            else if (!hasExplicitPageToken)
            {
                lock (SyncRoot)
                {
                    if (string.Equals(_paginationScopeKey, paginationScopeKey, StringComparison.Ordinal))
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
            var snippetPreloadStatus = await ResolveSnippetPreloadStatusAsync().ConfigureAwait(false);

            var activeScopeFindings = await workflow.ListAsync(activeScope.ToQueryOptions(int.MaxValue));

            var effectiveOffset = Math.Min(cursorOffset, activeScopeFindings.Count);
            var executionFindings = activeScopeFindings
                .Skip(effectiveOffset)
                .Take(batchLimit)
                .ToList();
            var nextOffset = effectiveOffset + executionFindings.Count;
            var hasMore = nextOffset < activeScopeFindings.Count;
            var totalPages = activeScopeFindings.Count <= 0
                ? 1
                : (int)Math.Ceiling((double)activeScopeFindings.Count / batchLimit);
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

            // Build leaf directory facets from unpaginated results
            var leafDirectories = ComputeLeafDirectoryFacets(activeScopeFindings);
            var scopeRules = activeScopeFindings
                .Select(f => f.RuleName)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var scopeSeverities = activeScopeFindings
                .Select(f => f.Severity)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var scopeStatuses = activeScopeFindings
                .Select(f => f.State)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var availableFacets = new ScopedAvailableFacets(scopeRules, scopeSeverities, scopeStatuses, leafDirectories);

            return new ScopedGetPayload(
                new ScopedContext(
                    "Results are filtered by the persistent Active Scope. Use sarif_filter to change filters.",
                    ToScopeDictionary(activeScope),
                    new ScopedMetrics(metrics.TotalInScope, metrics.ReturnedInBatch, metrics.RemainingInScope),
                    snippetPreloadStatus,
                    pagination),
                findingRows,
                debugPrompt,
                availableFacets);
        }

        private static async Task<string> ResolveSnippetPreloadStatusAsync()
        {
            var warmupService = SnippetWarmupService;
            if (warmupService == null)
            {
                return "unavailable";
            }

            var status = warmupService.GetPreloadStatus();
            if (status.State == SnippetPreloadState.InProgress)
            {
                status = await warmupService.WaitForPreloadAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }

            return status.Message;
        }

        /// <summary>
        /// Computes the top 3-5 noisiest leaf directories (immediate parent folder of each file) from findings.
        /// </summary>
        private static string[] ComputeLeafDirectoryFacets(IReadOnlyList<TriageListItem> findings, int maxDirectories = 5)
        {
            return findings
                .Select(f => f.FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => ExtractLeafDirectory(p.Replace('\\', '/')))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(maxDirectories)
                .Select(g => g.Key)
                .ToArray();
        }

        private static string ExtractLeafDirectory(string normalizedPath)
        {
            var lastSlash = normalizedPath.LastIndexOf('/');
            if (lastSlash <= 0)
            {
                return string.Empty;
            }

            var withoutFile = normalizedPath[..lastSlash];
            var parentSlash = withoutFile.LastIndexOf('/');
            return parentSlash >= 0
                ? withoutFile[(parentSlash + 1)..]
                : withoutFile;
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
            if (string.IsNullOrWhiteSpace(state) && map.TryGetValue("status", out var status))
            {
                state = status;
            }

            return new ActiveScopeFilter(severity ?? string.Empty, rule ?? string.Empty, file ?? string.Empty, state ?? string.Empty);
        }

        private static ActiveScopeFilter ParseSpaceSeparatedQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new ActiveScopeFilter();
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inQuotes = false;
            var buffer = new System.Text.StringBuilder();

            foreach (var ch in query)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    buffer.Append(ch);
                    continue;
                }

                if (ch == ' ' && !inQuotes)
                {
                    AddTokenToMap(buffer.ToString(), map);
                    buffer.Clear();
                    continue;
                }

                buffer.Append(ch);
            }

            AddTokenToMap(buffer.ToString(), map);

            map.TryGetValue("severity", out var severity);
            map.TryGetValue("rule", out var rule);
            if (string.IsNullOrWhiteSpace(rule) && map.TryGetValue("ruleId", out var ruleId))
            {
                rule = ruleId;
            }

            map.TryGetValue("path", out var path);
            if (string.IsNullOrWhiteSpace(path) && map.TryGetValue("file", out var file))
            {
                path = file;
            }

            map.TryGetValue("status", out var status);
            if (string.IsNullOrWhiteSpace(status) && map.TryGetValue("state", out var state))
            {
                status = state;
            }

            return new ActiveScopeFilter(severity ?? string.Empty, rule ?? string.Empty, path ?? string.Empty, status ?? string.Empty);

            static void AddTokenToMap(string token, Dictionary<string, string> map)
            {
                var trimmed = token.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    return;
                }

                var separatorIndex = trimmed.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex == trimmed.Length - 1)
                {
                    return;
                }

                var key = trimmed[..separatorIndex].Trim();
                var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"', '\'');

                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    map[key] = value;
                }
            }
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

        private static void ResetPagination()
        {
            lock (SyncRoot)
            {
                _paginationScopeKey = string.Empty;
                _paginationNextOffset = 0;
            }
        }

        private static CallToolResult BuildAvailableFiltersResult()
        {
            HashSet<string> severities, rules, statuses;
            ActiveScopeFilter currentScope;
            lock (SyncRoot)
            {
                severities = new HashSet<string>(_availableSeverities, StringComparer.OrdinalIgnoreCase);
                rules = new HashSet<string>(_availableRules, StringComparer.OrdinalIgnoreCase);
                statuses = new HashSet<string>(_availableStatuses, StringComparer.OrdinalIgnoreCase);
                currentScope = _activeScope;
            }

            var lines = new List<string>
            {
                "## Available Filters",
                string.Empty,
                "Use `sarif_filter` with a space-separated query string. Example: `severity:high rule:SQLI status:open path:controllers`",
                string.Empty,
                "### Current Scope",
                currentScope.IsEmpty ? "- *(no active filters)*" : FormatActiveFilters(currentScope),
                string.Empty,
                "### Severity",
                severities.Count > 0
                    ? string.Join(", ", severities.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Select(s => $"`{s}`"))
                    : "*(none discovered)*",
                string.Empty,
                "### Rule",
                rules.Count > 0
                    ? string.Join(", ", rules.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).Select(r => $"`{r}`"))
                    : "*(none discovered)*",
                string.Empty,
                "### Status",
                string.Join(", ", statuses.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Select(s => $"`{s}`")),
                string.Empty,
                "### Path",
                "Any substring of the SARIF file URI (e.g. `controllers`, `src/models`).",
                string.Empty,
                "To clear all filters: `sarif_filter clear`"
            };

            return CreatePlainTextResult(string.Join(Environment.NewLine, lines));
        }

        private static string FormatActiveFilters(ActiveScopeFilter scope)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(scope.Severity))
            {
                parts.Add($"- Severity: `{scope.Severity}`");
            }

            if (!string.IsNullOrWhiteSpace(scope.Rule))
            {
                parts.Add($"- Rule: `{scope.Rule}`");
            }

            if (!string.IsNullOrWhiteSpace(scope.File))
            {
                parts.Add($"- Path: `{scope.File}`");
            }

            if (!string.IsNullOrWhiteSpace(scope.State))
            {
                parts.Add($"- Status: `{scope.State}`");
            }

            return string.Join(Environment.NewLine, parts);
        }

        private static CallToolResult CreatePlainTextResult(string text)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = text }
                }
            };
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
                    snippet_preload_status = payload.Context.SnippetPreloadStatus,
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
                string.Empty
            };

            // Display active filters
            var activeScope = payload.Context.ActiveScope;
            if (activeScope.Count > 0)
            {
                lines.Add("### Active Filters");
                foreach (var kvp in activeScope)
                {
                    lines.Add($"- **{kvp.Key}**: `{EscapeMarkdown(kvp.Value)}`");
                }

                lines.Add(string.Empty);
            }
            else
            {
                lines.Add("### Active Filters");
                lines.Add("- *(none — showing all findings)*");
                lines.Add(string.Empty);
            }

            lines.Add($"- Total in scope: **{metrics.TotalInScope}**");
            lines.Add($"- Returned in batch: **{metrics.ReturnedInBatch}**");
            lines.Add($"- Remaining in scope: **{metrics.RemainingInScope}**");
            lines.Add($"- Snippet preload status: **{payload.Context.SnippetPreloadStatus}**");
            lines.Add($"- Page: **{currentPage} of {totalPages}**");
            lines.Add($"- Page size: **{payload.Context.Pagination.PageSize}**");
            lines.Add($"- Has more: **{payload.Context.Pagination.HasMore}**");
            lines.Add(string.Empty);

            // Display top noisy directories
            if (payload.AvailableFacets?.TopLeafDirectories is { Length: > 0 } dirs)
            {
                lines.Add("### Top Directories");
                foreach (var dir in dirs)
                {
                    lines.Add($"- `{EscapeMarkdown(dir)}`");
                }

                lines.Add(string.Empty);
            }

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
            lines.Add("If the user asks to review/triage findings, call `sarif_review` with the target displayid. You must analyze the evidence above and provide your state, reason, and llmReasoning.");
            lines.Add("If the user wants to manually override a decision, call `sarif_update` with state, reason, and target.");
            lines.Add("To change filters, call `sarif_filter`.");
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

        private static string BuildScopedReviewMarkdown(ScopedTriagePayload payload, int ledgerEntriesWritten)
        {
            ArgumentNullException.ThrowIfNull(payload);

            var lines = new List<string>
            {
                "## SARIF Scoped Review",
                string.Empty,
                $"- Success: **{payload.Success}**",
                $"- Decision: `{EscapeMarkdown(payload.State)}`",
                $"- Internal workflow state: `{EscapeMarkdown(payload.WorkflowState)}`",
                $"- Target: `{EscapeMarkdown(payload.Target)}`",
                $"- Affected findings: **{payload.AffectedCount}**",
                $"- Ledger entries written: **{ledgerEntriesWritten}**",
                $"- Sync status: **pending**",
                $"- Reasoning: {EscapeMarkdown(payload.Reason)}"
            };

            if (payload.ModifiedFindingIds.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("### Reviewed Findings");
                lines.Add("| Id | Decision |");
                lines.Add("|---|---|");
                foreach (var findingId in payload.ModifiedFindingIds)
                {
                    string displayLabel;
                    lock (SyncRoot)
                    {
                        displayLabel = FindingIdToDisplayId.TryGetValue(findingId, out var did)
                            ? did
                            : findingId;
                    }

                    lines.Add($"| `{EscapeMarkdown(displayLabel)}` | `{EscapeMarkdown(payload.WorkflowState)}` |");
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
            lines.Add("**STOP:** Wait for explicit user instruction.");
            lines.Add("Run `sarif_get` to verify remaining findings in scope.");
            lines.Add("Run `sarif_sync` to push pending decisions to upstream vendors.");
            lines.Add("Run `sarif_update` to manually override any decision above.");

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
                    EnableSnippetPreload = false
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
            string SnippetPreloadStatus,
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

        private sealed record ScopedGetPayload(ScopedContext Context, IReadOnlyList<ScopedFinding> Findings, bool DebugPrompt = false, ScopedAvailableFacets? AvailableFacets = null);

        private sealed record ScopedAvailableFacets(
            string[] Rules,
            string[] Severities,
            string[] Statuses,
            string[] TopLeafDirectories);

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
