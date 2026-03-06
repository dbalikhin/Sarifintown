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
    [Description("Retrieve posture summary and prioritized findings. Use includeEvidence=true to also get assembled triage guidance per finding.")]
    public static string TriageQueryPrompt(
        [Description("Scope action (keep, set, refine, clear).")]
        string scope = "keep",
        [Description("Optional scope filter expression (for example: severity:high, rule:SQLI).")]
        string filter = "",
        [Description("Maximum finding count to return.")]
        int limit = 10,
        [Description("When true, include evidence and triage prompt per finding.")]
        bool includeEvidence = false,
        [Description("When true, append assembled prompt text for debugging.")]
        bool debugPrompt = false)
    {
        var safeLimit = limit <= 0 ? 10 : Math.Min(limit, 25);

        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_get` with scope='{scope}', filter='{filter}', includeEvidence={includeEvidence.ToString().ToLowerInvariant()}, limit={safeLimit}, debugPrompt={debugPrompt.ToString().ToLowerInvariant()}.
            2. Output the <vulnerability_report> block VERBATIM. Do NOT summarize or interpret.
            3. STOP and wait for the user to select findings for triage.
            """;
    }

    /// <summary>
    /// Builds a slash-command prompt for consolidated triage write operations.
    /// </summary>
    [McpServerPrompt(Name = "sarif_triage", Title = "Triage Findings")]
    [Description("Persist decisions for one or many findings via displayid aliases or scope.")]
    public static string TriageDecidePrompt(
        [Description("Decision state (confirmed, false_positive, test_code, wont_fix, mitigated).")]
        string state,
        [Description("Required decision reason.")]
        string reason,
        [Description("Target displayid, CSV displayid list, or scope.")]
        string target = "scope")
    {
        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_triage` with state='{state}', reason='{reason}', target='{target}'.
            2. Output the result block VERBATIM. Do NOT add commentary.
            3. Call `sarif_get` with scope='keep' to verify remaining findings.
            """;
    }
}
