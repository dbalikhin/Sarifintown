using ModelContextProtocol.Server;
using Sarifintown.Core;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.ComponentModel;
using System.Text.Json;

namespace Sarifintown.AgentEngine
{
    [McpServerToolType]
    public static class SarifTools
    {
        // Dependencies to be injected at startup in Program.cs
        public static IFileReader? FileReader { get; set; }
        public static ITreeSitterEngine? TreeSitterEngine { get; set; }

        [McpServerTool]
        [Description("Parses a SARIF file and filters security issues by category, severity, or rule ID. Returns a JSON list of matching issues.")]
        public static async Task<string> LoadAndFilterSarif(
            string sarifPath,
            string severity = "",
            string ruleId = "",
            string category = "")
        {
            if (FileReader == null) throw new InvalidOperationException("FileReader is not initialized.");

            if (!File.Exists(sarifPath))
                return JsonSerializer.Serialize(new { error = $"File not found: {sarifPath}" });

            try
            {
                var content = await FileReader.ReadFileAsync(sarifPath);
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

            try
            {
                var content = await FileReader.ReadFileAsync(sarifPath);
                var sarifLog = JsonSerializer.Deserialize<SarifLog>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var allResults = sarifLog?.Runs.SelectMany(r => r.Results ?? Enumerable.Empty<Result>()).ToList();

                if (allResults == null || !int.TryParse(resultId, out int index) || index < 0 || index >= allResults.Count)
                    return JsonSerializer.Serialize(new { error = "Invalid resultId or result not found." });

                var targetResult = allResults[index];

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

                        var fullPath = Path.Combine(sourceCodeRoot, relativePath.Replace("file://", "").TrimStart('/'));
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
