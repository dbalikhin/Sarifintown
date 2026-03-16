namespace Sarifintown.AgentEngine.Configuration;

public sealed record SyncOptions
{
    public const string SectionName = "Sync";

    public string? SnykToken { get; init; }
    
    public string? SnykOrgId { get; init; }
    
    public string? GitHubToken { get; init; }

    public string? GitHubRepo { get; init; }
}
