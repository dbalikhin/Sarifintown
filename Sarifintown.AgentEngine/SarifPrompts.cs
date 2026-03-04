using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Sarifintown.AgentEngine;

[McpServerPromptType]
public static class SarifPrompts
{
    /// <summary>
    /// Builds a slash-command prompt that forces workspace SARIF discovery before analysis.
    /// </summary>
    [McpServerPrompt(Name = "sarif.list-files", Title = "List Discovered SARIF Files")]
    [Description("MUST: Use this slash command to enumerate discovered SARIF files before any SARIF-path-dependent action.")]
    public static string ListWorkspaceSarifFilesPrompt()
    {
        return "MUST call `analyze_sarif` with action='list_files' and use only returned file names/paths for follow-up tools.";
    }

    /// <summary>
    /// Builds a slash-command prompt that requests non-guided triage status.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.status", Title = "Triage Status")]
    [Description("Use this kickoff slash command to retrieve authoritative triage status counts and posture summary.")]
    public static string TriageStatusPrompt()
    {
        return "Call `manage_triage` with action='status' to retrieve current triage posture from SARIF findings and triage state.";
    }

    /// <summary>
    /// Builds a slash-command prompt that retrieves prioritized findings.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.list", Title = "List Prioritized Findings")]
    [Description("MUST: Use this kickoff slash command to retrieve prioritized findings with filters instead of inferring finding sets.")]
    public static string TriageListPrompt(
        [Description("Optional severity filter for the follow-up guided list step.")]
        string severity = "",
        [Description("Optional rule filter.")]
        string rule = "",
        [Description("Optional file path filter.")]
        string file = "",
        [Description("Optional triage state filter.")]
        string state = "",
        [Description("Maximum finding count to return.")]
        int limit = 10)
    {
        return $"Call `manage_triage` with action='list', filters='{{\"severity\":\"{severity}\",\"rule\":\"{rule}\",\"file\":\"{file}\",\"state\":\"{state}\",\"limit\":{limit}}}'.";
    }

    /// <summary>
    /// Builds a kickoff slash-command prompt that starts file analysis through the facade.
    /// </summary>
    [McpServerPrompt(Name = "sarif.analyze.file", Title = "Analyze SARIF File")]
    [Description("MUST: Use this kickoff slash command to start SARIF file analysis via the facade.")]
    public static string AnalyzeFilePrompt(
        [Description("SARIF file path or discovered filename.")]
        string sarifPath,
        [Description("Optional severity filter.")]
        string severity = "",
        [Description("Optional rule filter.")]
        string ruleId = "",
        [Description("Optional category keyword filter.")]
        string category = "")
    {
        return $"Call `analyze_sarif` with action='filter', sarifPath='{sarifPath}', filters='{{\"severity\":\"{severity}\",\"ruleId\":\"{ruleId}\",\"category\":\"{category}\"}}'.";
    }
}
