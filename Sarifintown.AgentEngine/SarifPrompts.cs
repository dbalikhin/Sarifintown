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
        return "MUST call `ListWorkspaceSarifFiles` and use only returned file names/paths for follow-up tools.";
    }

    /// <summary>
    /// Builds a slash-command prompt that starts guided triage status flow.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.status.guided", Title = "Guided Triage Status")]
    [Description("MUST: Start autonomous triage by calling TriageStatusGuided and following returned next_step/pause metadata.")]
    public static string TriageStatusGuidedPrompt()
    {
        return "MUST call `TriageStatusGuided`, render markdown verbatim, then follow `next_step` instructions exactly.";
    }

    /// <summary>
    /// Builds a slash-command prompt that requests non-guided triage status.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.status", Title = "Triage Status")]
    [Description("Use this slash command to retrieve authoritative triage status counts and posture summary.")]
    public static string TriageStatusPrompt()
    {
        return "Call `TriageStatus` to retrieve current triage posture from SARIF findings and triage state.";
    }

    /// <summary>
    /// Builds a slash-command prompt that retrieves prioritized findings.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.list", Title = "List Prioritized Findings")]
    [Description("MUST: Use this slash command to retrieve prioritized findings with filters instead of inferring finding sets.")]
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
        return $"""
        MUST call `TriageList` with:
        - severity='{severity}'
        - rule='{rule}'
        - file='{file}'
        - state='{state}'
        - limit={limit}
        """;
    }

    /// <summary>
    /// Builds a slash-command prompt that retrieves guided prioritized findings.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.list.guided", Title = "Guided List Prioritized Findings")]
    [Description("MUST: Use this slash command for autonomous guided finding lists and follow returned next_step metadata.")]
    public static string TriageListGuidedPrompt(
        [Description("Optional severity filter.")]
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
        return $"""
        MUST call `TriageListGuided` with:
        - severity='{severity}'
        - rule='{rule}'
        - file='{file}'
        - state='{state}'
        - limit={limit}
        Then render markdown exactly and pause for user input.
        """;
    }

    /// <summary>
    /// Builds a slash-command prompt that inspects one finding.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.inspect", Title = "Inspect Finding Evidence")]
    [Description("MUST: Use this slash command to retrieve authoritative technical evidence for a specific finding.")]
    public static string TriageInspectPrompt(
        [Description("Finding identifier from list output.")]
        string findingId,
        [Description("Optional evidence mode.")]
        string evidenceMode = "")
    {
        return $"Call `TriageInspect` with findingId='{findingId}', evidenceMode='{evidenceMode}'.";
    }

    /// <summary>
    /// Builds a slash-command prompt that inspects one finding in guided mode.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.inspect.guided", Title = "Guided Inspect Finding")]
    [Description("MUST: Use this slash command for guided evidence inspection and execute returned next_step exactly.")]
    public static string TriageInspectGuidedPrompt(
        [Description("Finding identifier selected from guided list output.")]
        string findingId,
        [Description("Evidence mode for guided inspection.")]
        string evidenceMode = "line-window-concatenated")
    {
        return $"""
        MUST call `TriageInspectGuided` with findingId='{findingId}', evidenceMode='{evidenceMode}'.
        Render markdown exactly and pause for user input.
        """;
    }

    /// <summary>
    /// Builds a slash-command prompt that records a single triage decision.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.apply", Title = "Apply Triage Decision")]
    [Description("MUST: Use this slash command to persist TP/FP decision for one finding.")]
    public static string TriagePrompt(
        [Description("Finding identifier.")]
        string findingId,
        [Description("Decision state (TP or FP).")]
        string state,
        [Description("Reason for triage decision.")]
        string reason,
        [Description("Decision author label.")]
        string author = "AI")
    {
        return $"Call `Triage` with findingId='{findingId}', state='{state}', reason='{reason}', author='{author}'.";
    }

    /// <summary>
    /// Builds a slash-command prompt that applies bulk triage decisions.
    /// </summary>
    [McpServerPrompt(Name = "sarif.triage.bulk", Title = "Apply Bulk Triage")]
    [Description("MUST: Use this slash command to apply TP/FP decisions to multiple findings using filters.")]
    public static string TriageBulkPrompt(
        [Description("Decision state (TP or FP).")]
        string state,
        [Description("Reason for triage decision.")]
        string reason,
        [Description("Optional severity filter.")]
        string severity = "",
        [Description("Optional rule filter.")]
        string rule = "",
        [Description("Optional file filter.")]
        string file = "",
        [Description("Set true for dry-run preview only.")]
        bool dryRun = false,
        [Description("Decision author label.")]
        string author = "AI")
    {
        return $"""
        Call `TriageBulk` with state='{state}', reason='{reason}', severity='{severity}', rule='{rule}', file='{file}', dryRun={dryRun.ToString().ToLowerInvariant()}, author='{author}'.
        """;
    }

    /// <summary>
    /// Builds a slash-command prompt that resolves interactive host surface.
    /// </summary>
    [McpServerPrompt(Name = "sarif.resolve-surface", Title = "Resolve Interactive Surface")]
    [Description("Use this slash command to resolve host-specific UI/TUI surface metadata before starting interactive flows.")]
    public static string ResolveInteractiveSurfacePrompt(
        [Description("Optional host hint override.")]
        string hostHint = "",
        [Description("When true, starts CLI menu when host resolves to terminal mode.")]
        bool startCliMenu = false)
    {
        return $"Call `ResolveInteractiveSurface` with hostHint='{hostHint}', startCliMenu={startCliMenu.ToString().ToLowerInvariant()}.";
    }

    /// <summary>
    /// Builds a slash-command prompt that loads and filters SARIF issues.
    /// </summary>
    [McpServerPrompt(Name = "sarif.load-filter", Title = "Load and Filter SARIF")]
    [Description("MUST: Use this slash command to parse SARIF and filter issues by severity/rule/category.")]
    public static string LoadAndFilterSarifPrompt(
        [Description("SARIF file path or discovered filename.")]
        string sarifPath,
        [Description("Optional severity filter.")]
        string severity = "",
        [Description("Optional rule filter.")]
        string ruleId = "",
        [Description("Optional category keyword filter.")]
        string category = "")
    {
        return $"Call `LoadAndFilterSarif` with sarifPath='{sarifPath}', severity='{severity}', ruleId='{ruleId}', category='{category}'.";
    }

    /// <summary>
    /// Builds a slash-command prompt that extracts code flow evidence.
    /// </summary>
    [McpServerPrompt(Name = "sarif.extract-flow", Title = "Extract Code Flow")]
    [Description("MUST: Use this slash command to extract full source-to-sink flow for one SARIF result.")]
    public static string ExtractCodeFlowPrompt(
        [Description("SARIF file path or discovered filename.")]
        string sarifPath,
        [Description("Result index from filtered output.")]
        string resultId,
        [Description("Workspace source root for path resolution.")]
        string sourceCodeRoot)
    {
        return $"Call `ExtractCodeFlow` with sarifPath='{sarifPath}', resultId='{resultId}', sourceCodeRoot='{sourceCodeRoot}'.";
    }

    /// <summary>
    /// Builds a slash-command prompt that generates markdown report from extracted flow.
    /// </summary>
    [McpServerPrompt(Name = "sarif.generate-report", Title = "Generate Analysis Report")]
    [Description("MUST: Use this slash command to produce markdown report output from extracted flow JSON.")]
    public static string GenerateAnalysisReportPrompt(
        [Description("Result identifier for metadata.")]
        string resultId,
        [Description("Flow data JSON from ExtractCodeFlow.")]
        string extractedFlowData,
        [Description("Output report path.")]
        string outputPath)
    {
        return $"Call `GenerateAnalysisReport` with resultId='{resultId}', extractedFlowData='{{...}}', outputPath='{outputPath}'.";
    }

    /// <summary>
    /// Builds a compatibility slash-command prompt for guided kickoff flow.
    /// </summary>
    [McpServerPrompt(Name = "sarif.force-check", Title = "Force Guided Check")]
    [Description("MUST: Compatibility prompt alias for guided triage kickoff.")]
    public static string SarifintownForceCheck(
        [Description("Optional severity filter.")]
        string severity = "",
        [Description("Optional rule filter.")]
        string rule = "",
        [Description("Maximum findings to return.")]
        int limit = 10)
    {
        return TriageListGuidedPrompt(severity, rule, file: "", state: "", limit);
    }

    /// <summary>
    /// Builds a compatibility slash-command prompt for guided inspection.
    /// </summary>
    [McpServerPrompt(Name = "sarif.inspect-finding", Title = "Inspect Finding (Guided)")]
    [Description("MUST: Compatibility prompt alias for guided finding inspection.")]
    public static string SarifintownInspectFinding(
        [Description("Finding identifier.")]
        string findingId,
        [Description("Evidence mode for inspection.")]
        string evidenceMode = "line-window-concatenated")
    {
        return TriageInspectGuidedPrompt(findingId, evidenceMode);
    }
}
