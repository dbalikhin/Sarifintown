namespace Sarifintown.AgentEngine;

public interface IPromptAssemblyService
{
    /// <summary>
    /// Builds a triage-ready LLM system prompt from modular markdown templates and finding context.
    /// </summary>
    Task<string> BuildTriagePromptAsync(string ruleId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds an aggregated LLM system prompt for multiple findings in a single string, avoiding duplication of global directives.
    /// </summary>
    Task<string> BuildBatchTriagePromptAsync(IEnumerable<(string RuleId, string Message)> findings, CancellationToken cancellationToken = default);
}
