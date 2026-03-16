using System.Text.Json.Serialization;

namespace Sarifintown.AgentEngine.Sync.Snyk;

internal sealed class SnykIgnorePayload
{
    [JsonPropertyName("data")]
    public SnykIgnoreData Data { get; set; } = new();
}

internal sealed class SnykIgnoreData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ignore";

    [JsonPropertyName("attributes")]
    public SnykIgnoreAttributes Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public SnykIgnoreRelationships Relationships { get; set; } = new();
}

internal sealed class SnykIgnoreAttributes
{
    [JsonPropertyName("reason_type")]
    public string ReasonType { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class SnykIgnoreRelationships
{
    [JsonPropertyName("issue")]
    public SnykIssueRelationship Issue { get; set; } = new();
}

internal sealed class SnykIssueRelationship
{
    [JsonPropertyName("data")]
    public SnykIssueData Data { get; set; } = new();
}

internal sealed class SnykIssueData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "issue";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal sealed class SnykErrorResponse
{
    [JsonPropertyName("errors")]
    public List<SnykErrorDetail> Errors { get; set; } = new();
}

internal sealed class SnykErrorDetail
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;
}
