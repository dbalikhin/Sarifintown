using ModelContextProtocol.Server;
using Sarifintown.Core;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Sarifintown.AgentEngine
{
    [McpServerToolType]
    public static class SarifTools
    {
        // Dependencies to be injected at startup in Program.cs
        public static IFileReader? FileReader { get; set; }
        public static ITreeSitterEngine? TreeSitterEngine { get; set; }
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
        [Description("Returns all SARIF files discovered in the current workspace .sarif folder at server startup.")]
        public static string ListWorkspaceSarifFiles()
        {
            List<string> files;
            lock (SyncRoot)
            {
                files = _discoveredSarifFiles.ToList();
            }

            var payload = files.Select(path => new
            {
                name = Path.GetFileName(path),
                path
            });

            return JsonSerializer.Serialize(payload);
        }

        [McpServerTool]
        [Description("Provides a 10,000-foot security posture summary from loaded SARIF findings and local .sarif/triage.json state.")]
        public static async Task<string> TriageStatus()
        {
            var workflow = CreateTriageWorkflowService();
            var status = await workflow.GetStatusAsync();
            return JsonSerializer.Serialize(status);
        }

        [McpServerTool]
        [Description("Returns prioritized findings with optional filters: severity, rule, file, state, and limit.")]
        public static async Task<string> TriageList(
            string severity = "",
            string rule = "",
            string file = "",
            string state = "",
            int limit = 10)
        {
            var workflow = CreateTriageWorkflowService();
            var findings = await workflow.ListAsync(new TriageQueryOptions(severity, rule, file, state, limit));
            return JsonSerializer.Serialize(findings);
        }

        [McpServerTool]
        [Description("Alias of TriageList for query-oriented clients.")]
        public static Task<string> TriageQuery(
            string severity = "",
            string rule = "",
            string file = "",
            string state = "",
            int limit = 10)
        {
            return TriageList(severity, rule, file, state, limit);
        }

        [McpServerTool]
        [Description("Returns dense technical evidence for a single finding including ordered data flow steps and method snippets.")]
        public static async Task<string> TriageInspect(string findingId)
        {
            var workflow = CreateTriageWorkflowService();
            var inspect = await workflow.InspectAsync(findingId);

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

        [McpServerTool]
        [Description("Records a TP/FP triage decision for one finding in .sarif/triage.json.")]
        public static async Task<string> Triage(
            string findingId,
            string state,
            string reason,
            string author = "AI")
        {
            var workflow = CreateTriageWorkflowService();
            var result = await workflow.TriageAsync(findingId, state, reason, author);
            return JsonSerializer.Serialize(result);
        }

        [McpServerTool]
        [Description("Applies TP/FP triage to multiple findings using list filters. At least one of severity/rule/file is required.")]
        public static async Task<string> TriageBulk(
            string state,
            string reason,
            string severity = "",
            string rule = "",
            string file = "",
            bool dryRun = false,
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

        [McpServerTool]
        [Description("Routes to the best interactive experience for the connected MCP host. Returns ui:// URI for IDE hosts and Spectre.Console TUI metadata for CLI hosts.")]
        public static string ResolveInteractiveSurface(
            McpServer thisServer,
            string hostHint = "",
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

        [McpServerTool]
        [Description("Parses a SARIF file and filters security issues by category, severity, or rule ID. Returns a JSON list of matching issues.")]
        public static async Task<string> LoadAndFilterSarif(
            string sarifPath,
            string severity = "",
            string ruleId = "",
            string category = "")
        {
            if (FileReader == null) throw new InvalidOperationException("FileReader is not initialized.");

            var resolvedSarifPath = ResolveSarifPath(sarifPath);

            if (!File.Exists(resolvedSarifPath))
                return JsonSerializer.Serialize(new { error = $"File not found: {sarifPath}" });

            try
            {
                var content = await FileReader.ReadFileAsync(resolvedSarifPath);
                var sarifLog = JsonSerializer.Deserialize<SarifLog>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (sarifLog?.Runs == null || !sarifLog.Runs.Any())
                    return JsonSerializer.Serialize(new { error = "Invalid or empty SARIF file." });

                var results = sarifLog.Runs.SelectMany(r => r.Results ?? Enumerable.Empty<Result>()).ToList();

                // Apply Filters
                if (!string.IsNullOrWhiteSpace(severity))
                    results = results.Where(r => string.Equals(r.Level, severity, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!string.IsNullOrWhiteSpace(ruleId))
                    results = results.Where(r => string.Equals(r.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!string.IsNullOrWhiteSpace(category))
                    results = results.Where(r => r.Message?.Text?.Contains(category, StringComparison.OrdinalIgnoreCase) == true ||
                                                 r.RuleId?.Contains(category, StringComparison.OrdinalIgnoreCase) == true).ToList();

                var simplifiedResults = results.Select((r, index) => new
                {
                    result_id = index.ToString(),
                    rule_id = r.RuleId,
                    level = r.Level ?? "warning",
                    message = r.Message?.Text,
                    location = r.Locations?.FirstOrDefault()?.PhysicalLocation?.ArtifactLocation?.Uri
                });

                return JsonSerializer.Serialize(simplifiedResults);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to parse SARIF: {ex.Message}" });
            }
        }

        [McpServerTool]
        [Description("Extracts the full data flow (source to sink) for a specific SARIF issue using Tree-sitter AST extraction. Returns a JSON trace.")]
        public static async Task<string> ExtractCodeFlow(
            string sarifPath,
            string resultId,
            string sourceCodeRoot)
        {
            if (FileReader == null || TreeSitterEngine == null)
                throw new InvalidOperationException("Core engines are not initialized.");

            var resolvedSarifPath = ResolveSarifPath(sarifPath);

            try
            {
                if (!File.Exists(resolvedSarifPath))
                    return JsonSerializer.Serialize(new { error = $"File not found: {sarifPath}" });

                var content = await FileReader.ReadFileAsync(resolvedSarifPath);
                var sarifLog = JsonSerializer.Deserialize<SarifLog>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var flattenedResults = sarifLog?.Runs
                    .SelectMany(run => (run.Results ?? Enumerable.Empty<Result>()).Select(result => (run, result)))
                    .ToList();

                if (flattenedResults == null || !int.TryParse(resultId, out int index) || index < 0 || index >= flattenedResults.Count)
                    return JsonSerializer.Serialize(new { error = "Invalid resultId or result not found." });

                var targetPair = flattenedResults[index];
                var targetResult = targetPair.result;
                var targetRun = targetPair.run;

                if (targetResult.CodeFlows == null || !targetResult.CodeFlows.Any())
                    return JsonSerializer.Serialize(new { message = "No code flow trace is available in the SARIF log for this issue." });

                var flowSteps = new List<object>();
                var threadFlows = targetResult.CodeFlows.SelectMany(cf => cf.ThreadFlows).ToList();

                foreach (var threadFlow in threadFlows)
                {
                    foreach (var location in threadFlow.Locations)
                    {
                        var physLoc = location.Location?.PhysicalLocation;
                        if (physLoc == null) continue;

                        var relativePath = physLoc.ArtifactLocation?.Uri;
                        if (string.IsNullOrEmpty(relativePath)) continue;

                        var resolvedPath = FileHelper.ResolveArtifactPath(physLoc.ArtifactLocation, targetRun);
                        var fullPath = Path.IsPathRooted(resolvedPath)
                            ? resolvedPath
                            : Path.Combine(sourceCodeRoot, (resolvedPath ?? relativePath).Replace("file://", "").TrimStart('/'));

                        string snippetCode = "Source file unavailable";

                        if (File.Exists(fullPath))
                        {
                            var sourceCode = await FileReader.ReadFileAsync(fullPath);

                            // Leverage TreeSitter for accurate code parsing
                            var language = GetLanguageFromExtension(Path.GetExtension(fullPath));

                            int startLine = (physLoc.Region?.StartLine ?? 1) - 1;
                            int endLine = (physLoc.Region?.EndLine ?? startLine + 1) - 1;

                            snippetCode = await TreeSitterEngine.ExtractMethodAsync(sourceCode, language, startLine, endLine);

                            if (string.IsNullOrEmpty(snippetCode))
                            {
                                // Basic snippet extraction (expandable with TreeSitter nodes)
                                var lines = sourceCode.Split('\n');
                                startLine = Math.Max(0, startLine);
                                endLine = Math.Min(lines.Length - 1, endLine);

                                snippetCode = string.Join("\n", lines.Skip(startLine).Take(endLine - startLine + 1));
                            }
                        }

                        flowSteps.Add(new
                        {
                            file_path = relativePath,
                            start_line = physLoc.Region?.StartLine,
                            message = location.Location?.Message?.Text,
                            code_snippet = snippetCode.Trim()
                        });
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    rule_id = targetResult.RuleId,
                    flow_steps = flowSteps
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to extract code flow: {ex.Message}" });
            }
        }

        private static string GetLanguageFromExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".cs" => "csharp",
                ".js" => "javascript",
                ".ts" => "typescript",
                ".py" => "python",
                ".java" => "java",
                ".cpp" => "cpp",
                ".c" => "c",
                ".go" => "go",
                ".rs" => "rust",
                ".rb" => "ruby",
                ".php" => "php",
                ".html" => "html",
                ".css" => "css",
                ".json" => "json",
                ".xml" => "xml",
                ".yaml" => "yaml",
                ".yml" => "yaml",
                ".md" => "markdown",
                ".sh" => "bash",
                ".ps1" => "powershell",
                ".sql" => "sql",
                _ => "csharp" // default
            };
        }

        private static string ResolveSarifPath(string sarifPath)
        {
            if (Path.IsPathRooted(sarifPath))
            {
                return sarifPath;
            }

            lock (SyncRoot)
            {
                var matchByFullPath = _discoveredSarifFiles.FirstOrDefault(path =>
                    string.Equals(path, sarifPath, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(matchByFullPath))
                {
                    return matchByFullPath;
                }

                var matchByFileName = _discoveredSarifFiles.FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), sarifPath, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(matchByFileName))
                {
                    return matchByFileName;
                }
            }

            return sarifPath;
        }

        private static TriageWorkflowService CreateTriageWorkflowService()
        {
            if (FileReader == null || TreeSitterEngine == null)
            {
                throw new InvalidOperationException("Core engines are not initialized.");
            }

            List<string> discoveredFiles;
            string workspaceRoot;

            lock (SyncRoot)
            {
                discoveredFiles = _discoveredSarifFiles.ToList();
                workspaceRoot = _workspaceRoot;
            }

            return new TriageWorkflowService(FileReader, TreeSitterEngine, workspaceRoot, discoveredFiles);
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

        [McpServerTool]
        [Description("Compiles extracted JSON data flow into a formatted markdown file (result.md) to be used for secondary AI analysis.")]
        public static string GenerateAnalysisReport(
            string resultId,
            string extractedFlowData,
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
