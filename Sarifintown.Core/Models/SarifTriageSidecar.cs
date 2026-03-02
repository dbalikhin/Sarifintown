using System.Text.Json.Serialization;

namespace Sarifintown.Models
{
    public sealed record SarifTriageSidecar
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; } = 1;

        [JsonPropertyName("suppressions")]
        public List<SarifTriageSuppressionEntry> Suppressions { get; init; } = new();
    }

    public sealed record SarifTriageSuppressionEntry
    {
        [JsonPropertyName("identity")]
        public string Identity { get; init; } = string.Empty;

        [JsonPropertyName("ruleId")]
        public string RuleId { get; init; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; init; } = string.Empty;

        [JsonPropertyName("startLine")]
        public int? StartLine { get; init; }

        [JsonPropertyName("updatedUtc")]
        public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;

        [JsonPropertyName("suppression")]
        public Suppression Suppression { get; init; } = new();
    }
}
