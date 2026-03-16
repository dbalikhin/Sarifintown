using System.Text.Json.Serialization;

namespace Sarifintown.AgentEngine;

/// <summary>
/// Valid triage decision states for the review ledger.
/// Maps to upstream vendor suppression/disposition states.
/// </summary>
internal enum TriageDecisionState
{
    [JsonStringEnumMemberName("false_positive")]
    FalsePositive,

    [JsonStringEnumMemberName("wont_fix")]
    WontFix,

    [JsonStringEnumMemberName("test_code")]
    TestCode,

    [JsonStringEnumMemberName("confirmed")]
    Confirmed,

    [JsonStringEnumMemberName("mitigated")]
    Mitigated
}

/// <summary>
/// Sync status for the upstream publish queue.
/// </summary>
internal enum UpstreamSyncStatus
{
    [JsonStringEnumMemberName("pending")]
    Pending,

    [JsonStringEnumMemberName("synced")]
    Synced,

    [JsonStringEnumMemberName("skipped")]
    Skipped,

    [JsonStringEnumMemberName("failed")]
    Failed
}

/// <summary>
/// Identity and routing metadata for a triaged finding.
/// </summary>
internal sealed record LedgerMetadata
{
    [JsonPropertyName("finding_id")]
    public string FindingId { get; init; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("rule_id")]
    public string RuleId { get; init; } = string.Empty;

    [JsonPropertyName("file_path")]
    public string FilePath { get; init; } = string.Empty;
}

/// <summary>
/// The active triage decision for a finding.
/// </summary>
internal sealed record LedgerTriageDecision
{
    [JsonPropertyName("state")]
    public TriageDecisionState State { get; init; }

    [JsonPropertyName("short_reason")]
    public string ShortReason { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = "AI";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Audit trail capturing the LLM input and reasoning for reproducibility.
/// </summary>
internal sealed record LedgerAuditLog
{
    [JsonPropertyName("input_markdown")]
    public string InputMarkdown { get; init; } = string.Empty;

    [JsonPropertyName("llm_reasoning")]
    public string LlmReasoning { get; init; } = string.Empty;

    [JsonPropertyName("human_reviewed")]
    public bool HumanReviewed { get; init; }

    /// <summary>
    /// The assembled organizational rules prompt injected via sarif_review. Null for human overrides.
    /// </summary>
    [JsonPropertyName("system_prompt_used")]
    public string? SystemPromptUsed { get; init; }
}

/// <summary>
/// Upstream vendor sync state machine for a ledger entry.
/// </summary>
internal sealed record LedgerUpstreamSync
{
    [JsonPropertyName("status")]
    public UpstreamSyncStatus Status { get; init; } = UpstreamSyncStatus.Pending;

    [JsonPropertyName("target_platform")]
    public string TargetPlatform { get; init; } = string.Empty;

    [JsonPropertyName("last_sync_attempt")]
    public DateTime? LastSyncAttempt { get; init; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// A single ledger entry combining metadata, decision, audit, and sync state.
/// </summary>
internal sealed record LedgerEntry
{
    [JsonPropertyName("record_id")]
    public Guid RecordId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("partition_key")]
    public string PartitionKey { get; init; } = string.Empty;

    [JsonPropertyName("finding_id")]
    public string FindingId
    {
        get => Metadata.FindingId;
        init => Metadata = Metadata with { FindingId = value ?? string.Empty };
    }

    [JsonPropertyName("local_state")]
    public TriageDecisionState LocalState
    {
        get => TriageDecision.State;
        init => TriageDecision = TriageDecision with { State = value };
    }

    [JsonPropertyName("upstream_provider")]
    public string UpstreamProvider { get; init; } = string.Empty;

    [JsonPropertyName("sync_status")]
    public UpstreamSyncStatus SyncStatus
    {
        get => UpstreamSync.Status;
        init => UpstreamSync = UpstreamSync with { Status = value };
    }

    [JsonPropertyName("sync_error_message")]
    public string? SyncErrorMessage
    {
        get => UpstreamSync.ErrorMessage;
        init => UpstreamSync = UpstreamSync with { ErrorMessage = value };
    }

    [JsonPropertyName("upstream_state")]
    public string UpstreamState { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public LedgerMetadata Metadata { get; init; } = new();

    [JsonPropertyName("triage_decision")]
    public LedgerTriageDecision TriageDecision { get; init; } = new();

    [JsonPropertyName("audit_log")]
    public LedgerAuditLog AuditLog { get; init; } = new();

    [JsonPropertyName("upstream_sync")]
    public LedgerUpstreamSync UpstreamSync { get; init; } = new();
}

/// <summary>
/// Root document for the triage ledger.
/// Keys are composite strings: "{tool_name}:{finding_id}".
/// </summary>
internal sealed record TriageLedgerDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("entries")]
    public Dictionary<string, LedgerEntry> Entries { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds the composite key used for O(1) lookups across different scanner tools.
    /// </summary>
    public static string BuildCompositeKey(string toolName, string findingId)
    {
        var normalizedTool = string.IsNullOrWhiteSpace(toolName)
            ? "unknown-tool"
            : toolName.Trim().ToLowerInvariant();

        return $"{normalizedTool}:{findingId}";
    }
}
