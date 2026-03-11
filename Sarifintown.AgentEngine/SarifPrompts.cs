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
    [Description("Load a lightweight paginated findings index (ID, Rule, Severity, File Path). Call this FIRST in the sequential flow before sarif_review and sarif_update.")]
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
    /// Builds a slash-command prompt for loading code evidence and organizational rules before triage.
    /// The LLM receives deep code flows and the assembled organizational rules, then calls sarif_update.
    /// </summary>
    [McpServerPrompt(Name = "sarif_review", Title = "Review Findings")]
    [Description("Load deep code evidence and organizational rules for specific findings. Call this FIRST to analyze a vulnerability.")]
    public static string ReviewPrompt(
        [Description("Target displayid, CSV displayid list, or 'scope' (max 25).")]
        string target = "scope")
    {
        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_get` to obtain the list of findings and their displayids.
            2. Call `sarif_review` with target='{target}' to load code evidence and organizational rules.
            3. Analyze the evidence using the rules in the <system_directive> block returned by sarif_review.
            4. Call `sarif_update` with target='{target}', state=<your decision>, reason=<your reason>, llmReasoning=<your full chain-of-thought>.
            5. Output exactly one <vulnerability_report> block VERBATIM from the sarif_update result. Do NOT summarize, interpret, or add commentary.
            6. STOP and wait for user instruction.
            """;
    }

    /// <summary>
    /// Builds a slash-command prompt for recording a triage decision.
    /// Handles both AI-driven triage (with llmReasoning) and human manual overrides (without llmReasoning).
    /// </summary>
    [McpServerPrompt(Name = "sarif_update", Title = "Update Triage Decision")]
    [Description("Record a triage decision. Call this AFTER analyzing the output of sarif_review.")]
    public static string UpdatePrompt(
        [Description("Decision state (confirmed, false_positive, test_code, wont_fix, mitigated).")]
        string state,
        [Description("Required decision reason.")]
        string reason,
        [Description("Target displayid, CSV displayid list, or 'scope'.")]
        string target = "scope",
        [Description("Optional AI chain-of-thought. Provide for AI triage. Omit for explicit human manual overrides.")]
        string llmReasoning = "")
    {
        return $"""
            EXECUTION PROTOCOL — follow these steps exactly:
            1. Call `sarif_get` to identify the exact target displayid if needed.
            2. Call `sarif_review` with target='{target}' if analysis evidence has not been loaded yet.
            3. Call `sarif_update` with target='{target}', state='{state}', reason='{reason}', and llmReasoning='{llmReasoning}'.
            4. Output the result block VERBATIM. Do NOT add commentary.
            5. Call `sarif_get` to verify remaining findings.
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
