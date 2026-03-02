namespace Sarifintown.AgentEngine;

internal enum TriageFindingState
{
    Open = 0,
    TP = 1,
    FP = 2
}

internal enum TriageEvidenceMode
{
    TreeSitterMethod = 0,
    LineWindowStrict = 1,
    LineWindowConcatenated = 2
}

internal sealed record TriageQueryOptions(
    string Severity = "",
    string Rule = "",
    string File = "",
    string State = "",
    int Limit = 10);

internal sealed record TriageStatusResult(
    int TotalFindings,
    IReadOnlyDictionary<string, int> SeverityCounts,
    IReadOnlyDictionary<string, int> RuleCounts,
    int OpenCount,
    int TriagedCount,
    int TruePositiveCount,
    int FalsePositiveCount);

internal sealed record TriageListItem(
    string FindingId,
    string RuleName,
    string FilePath,
    int? LineNumber,
    string Severity,
    double PriorityScore,
    string State);

internal sealed record TriageInspectStep(
    int Index,
    string FilePath,
    int? StartLine,
    string Message,
    string CodeSnippet);

internal sealed record TriageEvidenceBlock(
    int StartStepIndex,
    int EndStepIndex,
    string FilePath,
    int? StartLine,
    int? EndLine,
    string Mode,
    IReadOnlyList<int> StepIndexes,
    string CodeSnippet);

internal sealed record TriageInspectResult(
    string FindingId,
    string RuleId,
    string RuleName,
    string Severity,
    string State,
    string Message,
    string RuleDescription,
    string Remediation,
    IReadOnlyList<TriageInspectStep> DataFlowSteps,
    string DataFlowEvidenceMode,
    IReadOnlyList<TriageEvidenceBlock> DataFlowEvidenceBlocks);

internal sealed record TriageOperationResult(
    bool Success,
    string Message,
    string FindingId,
    string State,
    string Reason,
    string Author,
    DateTime UpdatedUtc);

internal sealed record TriageBulkResult(
    bool Success,
    string Message,
    int AffectedCount,
    IReadOnlyList<string> ModifiedFindingIds,
    bool DryRun);

internal sealed record TriageStateDocument
{
    public int SchemaVersion { get; init; } = 1;
    public List<TriageStateEntry> Entries { get; init; } = new();
}

internal sealed record TriageStateEntry
{
    public string FindingId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}
