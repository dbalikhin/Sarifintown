namespace Sarifintown.AgentEngine.Configuration;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    public string? SnykToken { get; init; }
    
    public string? SnykOrgId { get; init; }
    
    public string? GhasToken { get; init; }
    
    public string? GithubToken { get; init; }
}
