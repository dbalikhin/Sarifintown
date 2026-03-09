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
            3. Call `sarif_get` to verify remaining findings.
            """;
    }
}
