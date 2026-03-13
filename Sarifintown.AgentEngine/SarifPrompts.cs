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
    [Description("Set or clear filters for SARIF (SAST/Secret/SCA) findings/issues/vulnerabilities. Call with no query to list available filter values.")]
    public static string FilterPrompt(
        [Description("Space-separated filter query (e.g. 'severity:high rule:SQLI status:open path:controllers'). Omit to list available filters.")]
        string query = "")
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "List available filter values. Stop and wait for the user to choose.";
        }

        return $"Filter applied: query='{query}'. Proceed to call sarif_get to view the results.";
    }

    /// <summary>
    /// Builds a slash-command prompt for reading SARIF findings with current filters.
    /// </summary>
    [McpServerPrompt(Name = "sarif_get", Title = "Get Security Findings")]
    [Description("Load a lightweight SARIF (SAST/Secret/SCA) findings/issues/vulnerabilities index (ID, Rule, Severity, File Path). Call this to see what needs review.")]
    public static string TriageQueryPrompt(
        [Description("Maximum finding count to return.")]
        int limit = 10,
        [Description("Optional 1-based page number. When provided, it overrides automatic pagination and pageToken.")]
        int page = 0,
        [Description("Optional pagination token returned by a previous sarif_get response.")]
        string pageToken = "")
    {
        var safeLimit = limit <= 0 ? 10 : Math.Min(limit, 25);

        return $"Fetch up to {safeLimit} findings (page={page}, token='{pageToken}'). Output exactly one <vulnerability_report> block. Do not add commentary.";
    }

    /// <summary>
    /// Slash-command prompt for reviewing, analyzing, inspecting, or triaging SARIF findings.
    /// Loads deep code-flow evidence and organizational triage rules.
    /// </summary>
    [McpServerPrompt(Name = "sarif_review", Title = "Review Findings/Issues/Vulnerabilities (SAST/Secret/SCA)")]
    [Description("Provides deep code-flow evidence and organizational rules for a specific SARIF (SAST/Secret/SCA) finding/issue/vulnerability so you can review/triage/decide if it is a true or false positive.")]
    public static string ReviewPrompt(
        [Description("Target DisplayId (e.g. 1), CSV displayid list (e.g. 1,2,3), or 'scope' to review all open findings/issues (max 25).")]
        string target = "scope")
    {
        return $"Analyze the code evidence and rules for target='{target}'. Return your findings in a <system_directive> block. Wait for further instructions or proceed to record your decision.";
    }

    /// <summary>
    /// Builds a slash-command prompt for recording a triage decision.
    /// Handles both AI-driven triage (with llmReasoning) and human manual overrides (without llmReasoning).
    /// </summary>
    [McpServerPrompt(Name = "sarif_update", Title = "Update Triage Decision")]
    [Description("Record a triage decision for a SARIF (SAST/Secret/SCA) finding/issue/vulnerability. Call this AFTER analyzing the output of sarif_review.")]
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
        return $"Record decision for target='{target}': state='{state}', reason='{reason}'. Output the result VERBATIM.";
    }

    /// <summary>
    /// Builds a slash-command prompt for syncing pending triage decisions to upstream vendor APIs.
    /// </summary>
    [McpServerPrompt(Name = "sarif_sync", Title = "Sync Triage Decisions")]
    [Description("Push pending local review decisions for SARIF (SAST/Secret/SCA) findings/issues/vulnerabilities to upstream vendor APIs (e.g., Snyk, GitHub Advanced Security).")]
    public static string SyncPrompt(
        [Description("Target: 'pending' to sync all pending entries, or specific composite keys.")]
        string target = "pending")
    {
        return $"Sync triage decisions for target='{target}'. Output result VERBATIM.";
    }
}
