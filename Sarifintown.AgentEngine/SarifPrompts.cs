using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Sarifintown.AgentEngine;

[McpServerPromptType]
public static class SarifPrompts
{
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
    /// Builds a slash-command prompt to inspect one finding from the aggregated triage state.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.inspect", Title = "Inspect Finding")]
    [Description("MUST: Use this prompt to inspect technical evidence for one FindingId from triage list results.")]
    public static string TriageInspectPrompt(
        [Description("Finding identifier returned by triage listing commands.")]
        string findingId,
        [Description("Optional evidence mode override (line-window-strict, line-window-concatenated, tree-sitter-method).")]
        string evidenceMode = "")
    {
        return $"Call `manage_triage` with action='inspect', findingId='{findingId}', filters='{{\"evidenceMode\":\"{evidenceMode}\"}}'.";
    }

    /// <summary>
    /// Builds a slash-command prompt to apply TP/FP decision to one finding.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.apply", Title = "Apply Triage Decision")]
    [Description("MUST: Use this prompt to persist TP/FP for one finding via the triage facade.")]
    public static string TriageApplyPrompt(
        [Description("Finding identifier returned by triage listing commands.")]
        string findingId,
        [Description("Decision state TP or FP.")]
        string state,
        [Description("Required decision reason.")]
        string reason,
        [Description("Optional decision author label.")]
        string author = "AI")
    {
        return $"Call `manage_triage` with action='decide', findingId='{findingId}', state='{state}', reason='{reason}', filters='{{\"author\":\"{author}\"}}'.";
    }

    /// <summary>
    /// Builds a slash-command prompt to apply TP/FP decision to multiple findings.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.bulk", Title = "Bulk Triage Decision")]
    [Description("MUST: Use this prompt to run bulk TP/FP triage updates over filtered findings.")]
    public static string TriageBulkPrompt(
        [Description("Decision state TP or FP.")]
        string state,
        [Description("Required decision reason.")]
        string reason,
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
        return $"Call `manage_triage` with action='bulk_decide', state='{state}', reason='{reason}', filters='{{\"severity\":\"{severity}\",\"rule\":\"{rule}\",\"file\":\"{file}\",\"dryRun\":{dryRun.ToString().ToLowerInvariant()},\"author\":\"{author}\"}}'.";
    }
}
