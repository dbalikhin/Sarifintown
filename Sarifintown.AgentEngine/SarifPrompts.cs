using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Sarifintown.AgentEngine;

[McpServerPromptType]
public static class SarifPrompts
{
    /// <summary>
    /// Builds a slash-command prompt for consolidated triage read operations.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.query", Title = "Query Triage Posture")]
    [Description("MUST: Use this kickoff slash command to retrieve posture summary and prioritized findings in one response.")]
    public static string TriageQueryPrompt(
        [Description("Optional severity filter.")]
        string severity = "",
        [Description("Optional rule filter.")]
        string rule = "",
        [Description("Optional file path filter.")]
        string file = "",
        [Description("Optional triage state filter.")]
        string state = "",
        [Description("Maximum finding count to return.")]
        int limit = 10,
        [Description("When true, include evidence for returned findings.")]
        bool includeEvidence = false,
        [Description("Optional finding identifier for deep-dive mode.")]
        string findingId = "",
        [Description("Optional evidence mode override (line-window-strict, line-window-concatenated, tree-sitter-method).")]
        string evidenceMode = "")
    {
        return $"Call `manage_triage` with action='query', findingId='{findingId}', filters='{{\"severity\":\"{severity}\",\"rule\":\"{rule}\",\"file\":\"{file}\",\"state\":\"{state}\",\"limit\":{limit},\"includeEvidence\":{includeEvidence.ToString().ToLowerInvariant()},\"evidenceMode\":\"{evidenceMode}\"}}'.";
    }

    /// <summary>
    /// Builds a slash-command prompt for consolidated triage write operations.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.decide", Title = "Decide Triage State")]
    [Description("MUST: Use this prompt to persist TP/FP decisions for one or many findings via IDs or filters.")]
    public static string TriageDecidePrompt(
        [Description("Decision state TP or FP.")]
        string state,
        [Description("Required decision reason.")]
        string reason,
        [Description("Optional single finding identifier.")]
        string findingId = "",
        [Description("Optional comma-separated finding IDs for multi-target decisions.")]
        string findingIds = "",
        [Description("Optional severity filter.")]
        string severity = "",
        [Description("Optional rule filter.")]
        string rule = "",
        [Description("Optional file filter.")]
        string file = "",
        [Description("When true, preview affected findings without writing triage state.")]
        bool dryRun = false,
        [Description("Optional decision author label.")]
        string author = "AI")
    {
        return $"Call `manage_triage` with action='decide', findingId='{findingId}', state='{state}', reason='{reason}', filters='{{\"findingIds\":\"{findingIds}\",\"severity\":\"{severity}\",\"rule\":\"{rule}\",\"file\":\"{file}\",\"dryRun\":{dryRun.ToString().ToLowerInvariant()},\"author\":\"{author}\"}}'.";
    }
}
