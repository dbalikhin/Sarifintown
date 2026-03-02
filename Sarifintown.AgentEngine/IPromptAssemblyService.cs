namespace Sarifintown.AgentEngine;

public interface IPromptAssemblyService
{
    /// <summary>
    /// Builds a triage-ready LLM system prompt from modular markdown templates and finding context.
    /// </summary>
    Task<string> BuildTriagePromptAsync(string ruleId, string message, CancellationToken cancellationToken = default);
}
