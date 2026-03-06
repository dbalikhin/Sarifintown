using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Sarifintown.AgentEngine;

[McpServerPromptType]
public static class SarifPrompts
{
    /// <summary>
    /// Builds a slash-command prompt for consolidated triage read operations.
    /// </summary>
    [McpServerPrompt(Name = "sarif_get", Title = "Get Triage Posture")]
    [Description("MUST: Use this kickoff slash command to retrieve posture summary and prioritized findings in one response.")]
    public static string TriageQueryPrompt(
        [Description("Scope action (keep, set, refine, clear).")]
        string scope = "keep",
        [Description("Optional scope filter expression (for example: severity:high, rule:SQLI).")]
        string filter = "",
        [Description("Maximum finding count to return.")]
        int limit = 10,
        [Description("When true, include evidence for returned findings.")]
        bool includeEvidence = false)
    {
        var safeLimit = limit <= 0 ? 10 : Math.Min(limit, 25);

        return $"Call `sarif_get` with scope='{scope}', filter='{filter}', includeEvidence={includeEvidence.ToString().ToLowerInvariant()}, limit={safeLimit}.";
    }

    /// <summary>
    /// Builds a slash-command prompt for consolidated triage write operations.
    /// </summary>
    [McpServerPrompt(Name = "sarif_triage", Title = "Triage Findings")]
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
