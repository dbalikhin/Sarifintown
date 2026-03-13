namespace Sarifintown.AgentEngine.Configuration;

internal enum PreloadStrategy
{
    None = 0,
    LatestPerTool = 1,
    All = 2
}

internal sealed class SarifOptions
{
    public const string SectionName = "Sarif";

    public PreloadStrategy Strategy { get; set; } = PreloadStrategy.LatestPerTool;

    public bool EnableSnippetPreload { get; set; } = true;
}
