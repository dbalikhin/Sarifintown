using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Sarifintown.AgentEngine;

[McpServerPromptType]
public static class SarifPrompts
{
    /// <summary>
    /// Builds a slash-command prompt for setting SARIF finding filters.
    /// </summary>
    [McpServerPrompt(Name = "sarif_filter", Title = "Filter Findings")]
    [Description("Set or clear filters for SARIF findings. Call with no query to list available filter values.")]
    public static string FilterPrompt(
        [Description("Space-separated filter query (e.g. 'severity:high rule:SQLI status:open path:controllers'). Omit to list available filters.")]
        string query = "")
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return """
                EXECUTION PROTOCOL — follow these steps exactly:
                1. Call `sarif_filter` with no arguments to list available filter values.
                2. Output the result VERBATIM.
                3. STOP and wait for the user to choose filters.
                """;
        }

        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_filter` with query='{query}'.
            2. Output the confirmation VERBATIM.
            3. Call `sarif_get` to view filtered results.
            """;
    }

    /// <summary>
    /// Builds a slash-command prompt for reading SARIF findings with current filters.
    /// </summary>
    [McpServerPrompt(Name = "sarif_get", Title = "Get Triage Posture")]
    [Description("Retrieve posture summary and prioritized findings using the active filter.")]
    public static string TriageQueryPrompt(
        [Description("Maximum finding count to return.")]
        int limit = 10,
        [Description("Optional 1-based page number. When provided, it overrides automatic pagination and pageToken.")]
        int page = 0,
        [Description("Optional pagination token returned by a previous sarif_get response.")]
        string pageToken = "")
    {
        var safeLimit = limit <= 0 ? 10 : Math.Min(limit, 25);

        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_get` with limit={safeLimit}, page={page}, pageToken='{pageToken}'.
            2. Output exactly one <vulnerability_report> block VERBATIM. Do NOT summarize, interpret, duplicate, or append extra text.
            3. STOP immediately and wait for explicit user instruction.
            """;
    }

    /// <summary>
    /// Builds a slash-command prompt for autotriaging findings using LLM analysis.
    /// The LLM loads evidence, analyzes each finding, and autonomously determines the triage decision.
    /// </summary>
    [McpServerPrompt(Name = "sarif_review", Title = "Review Findings")]
    [Description("Autotriage the currently scoped findings. Loads evidence, analyzes each finding, determines a decision, and records it with full LLM reasoning into the local audit ledger.")]
    public static string ReviewPrompt(
        [Description("Target displayid, CSV displayid list, or 'scope' (max 25).")]
        string target = "scope")
    {
        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_get` to load findings with evidence for target='{target}'.
            2. For each finding in the result, analyze the evidence: code flow, snippets, rule description, severity.
            3. Determine the appropriate decision state (confirmed, false_positive, test_code, wont_fix, mitigated) and formulate a 1-2 sentence reason from the evidence.
            4. Call `sarif_review` with target='{target}', state=<your decision>, reason=<your reason>,
               llmReasoning=<your full chain-of-thought analysis>, inputMarkdown=<the evidence you analyzed>.
            5. Output exactly one <vulnerability_report> block VERBATIM from the result. Do NOT summarize, interpret, or add commentary.
            6. STOP and wait for user instruction.
            """;
    }

    /// <summary>
    /// Builds a slash-command prompt for manually overriding a triage decision.
    /// Sets human_reviewed=true in the audit ledger.
    /// </summary>
    [McpServerPrompt(Name = "sarif_update", Title = "Update Triage Decision")]
    [Description("Manually override a triage decision for one or many findings. Marks the decision as human-reviewed in the audit ledger.")]
    public static string UpdatePrompt(
        [Description("Decision state (confirmed, false_positive, test_code, wont_fix, mitigated).")]
        string state,
        [Description("Required decision reason.")]
        string reason,
        [Description("Target displayid, CSV displayid list, or 'scope'.")]
        string target = "scope")
    {
        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_update` with state='{state}', reason='{reason}', target='{target}'.
            2. Output the result block VERBATIM. Do NOT add commentary.
            3. Call `sarif_get` to verify remaining findings.
            """;
    }

    /// <summary>
    /// Builds a slash-command prompt for syncing pending triage decisions to upstream vendor APIs.
    /// </summary>
    [McpServerPrompt(Name = "sarif_sync", Title = "Sync Triage Decisions")]
    [Description("Push pending local review decisions to upstream vendor APIs (e.g., Snyk, GitHub Advanced Security).")]
    public static string SyncPrompt(
        [Description("Target: 'pending' to sync all pending entries, or specific composite keys.")]
        string target = "pending")
    {
        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_sync` with target='{target}'.
            2. Output the result VERBATIM. Do NOT add commentary.
            3. STOP and wait for user instruction.
            """;
    }
}
