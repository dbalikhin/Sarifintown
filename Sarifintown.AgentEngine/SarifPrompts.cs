using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Linq;

namespace Sarifintown.AgentEngine;

[McpServerPromptType]
public static class SarifPrompts
{
    /// <summary>
    /// Builds a slash-command prompt for consolidated triage read operations.
    /// </summary>
    [McpServerPrompt(Name = "sarif_triage_query", Title = "Query Triage Posture")]
    [Description("MUST: Use this kickoff slash command to retrieve posture summary and prioritized findings in one response.")]
    public static string TriageQueryPrompt(
        [Description("Scope action (keep, set, refine, clear).")]
        string scope = "keep",
        [Description("Optional scope filter expression (for example: severity:high, rule:SQLI).")]
        string filter = "",
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
        var effectiveFilter = string.IsNullOrWhiteSpace(filter)
            ? string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(severity) ? string.Empty : $"severity:{severity}",
                string.IsNullOrWhiteSpace(rule) ? string.Empty : $"rule:{rule}",
                string.IsNullOrWhiteSpace(file) ? string.Empty : $"file:{file}",
                string.IsNullOrWhiteSpace(state) ? string.Empty : $"state:{state}"
            }.Where(item => !string.IsNullOrWhiteSpace(item)))
            : filter;

        return $"Call `sarif_get` with scope='{scope}', filter='{effectiveFilter}', includeEvidence={includeEvidence.ToString().ToLowerInvariant()}, limit={limit}.";
    }

    /// <summary>
    /// Builds a slash-command prompt for consolidated triage write operations.
    /// </summary>
    [McpServerPrompt(Name = "sarif_triage_decide", Title = "Decide Triage State")]
    [Description("MUST: Use this prompt to persist decisions for one or many findings via displayid aliases or scope.")]
    public static string TriageDecidePrompt(
        [Description("Decision state (confirmed, false_positive, test_code, wont_fix, mitigated).")]
        string state,
        [Description("Required decision reason.")]
        string reason,
        [Description("Target displayid, CSV displayid list, or scope.")]
        string target = "scope")
    {
        return $"Call `sarif_triage` with state='{state}', reason='{reason}', target='{target}'.";
    }
}
