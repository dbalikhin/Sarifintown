namespace Sarifintown.AgentEngine;

public enum PromptTemplateStyle
{
    Structured = 0,
    Compact = 1,
    Verbose = 2
}

public sealed class PromptAssemblyOptions
{
    public const string SectionName = "PromptAssembly";

    public string? RootDirectoryPath { get; init; }

    public PromptTemplateStyle TemplateStyle { get; init; } = PromptTemplateStyle.Structured;

    public bool EnableSastModule { get; init; } = true;
    public bool EnableSecretModule { get; init; } = true;
    public bool EnableScaModule { get; init; } = true;
}
