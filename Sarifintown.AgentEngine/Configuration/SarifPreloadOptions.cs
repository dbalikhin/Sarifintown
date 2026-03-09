namespace Sarifintown.AgentEngine.Configuration;

internal enum PreloadStrategy
{
    None = 0,
    LatestPerTool = 1,
    All = 2
}

internal sealed class SarifPreloadOptions
{
    public const string SectionName = "Preload";

    public PreloadStrategy Strategy { get; set; } = PreloadStrategy.LatestPerTool;

    public bool EnableSnippetPreload { get; set; } = true;
}
