using System.Text.Json.Serialization;

namespace Sarifintown.AgentEngine.Sync.Snyk;

internal sealed class SnykPolicyPayload
{
    [JsonPropertyName("data")]
    public SnykPolicyData Data { get; set; } = new();
}

internal sealed class SnykPolicyData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "policy";

    [JsonPropertyName("attributes")]
    public SnykPolicyAttributes Attributes { get; set; } = new();
}

internal sealed class SnykPolicyAttributes
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = "ignore";

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonPropertyName("action")]    
    public SnykPolicyAction Action { get; set; } = new();

    [JsonPropertyName("conditions_group")]
    public SnykPolicyConditionsGroup ConditionsGroup { get; set; } = new();
}

internal sealed class SnykPolicyAction
{
    [JsonPropertyName("data")]
    public SnykPolicyActionData Data { get; set; } = new();
}

internal sealed class SnykPolicyActionData
{
    [JsonPropertyName("ignore_type")]
    public string IgnoreType { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("expires")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expires { get; set; }
}

internal sealed class SnykPolicyConditionsGroup
{
    [JsonPropertyName("logical_operator")]
    public string LogicalOperator { get; set; } = "and";

    [JsonPropertyName("conditions")]
    public List<SnykPolicyCondition> Conditions { get; set; } = new();
}

internal sealed class SnykPolicyCondition
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
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
