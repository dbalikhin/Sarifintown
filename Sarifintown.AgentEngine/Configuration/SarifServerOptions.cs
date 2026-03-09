namespace Sarifintown.AgentEngine.Configuration;

internal sealed class SarifServerOptions
{
    public const string SectionName = "Server";

    public bool EnableDebugPrompt { get; set; } = false;

    public bool IncludeEvidenceByDefault { get; set; } = true;
}
