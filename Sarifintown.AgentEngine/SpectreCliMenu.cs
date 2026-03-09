using Spectre.Console;
using ModelContextProtocol.Protocol;
using System.Linq;
using System.Text.Json;

namespace Sarifintown.AgentEngine;

internal static class SpectreCliMenu
{
    private static readonly string[] Actions =
    [
        "Triage status",
        "Triage list",
        "Triage inspect",
        "Triage decision",
        "Triage bulk",
        "List SARIF files",
        "Load and filter SARIF",
        "Extract code flow",
        "Generate analysis report",
        "Exit"
    ];

    public static string Start()
    {
        try
        {
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Sarifintown CLI Menu[/]")
                    .PageSize(10)
                    .AddChoices(Actions));

            return action;
        }
        catch (InvalidOperationException)
        {
            return "non-interactive-terminal";
        }
    }

    public static async Task<string> ExecuteTriageActionAsync(
        string action,
        CancellationToken cancellationToken = default)
    {
        return action switch
        {
            "Triage status" => await RenderStatusAsync(cancellationToken),
            "Triage list" => await RenderListAsync(cancellationToken),
            "Triage decision" => await RenderTriageAsync(cancellationToken),
            "Triage inspect" or "Triage bulk" => JsonSerializer.Serialize(new { success = false, message = $"Action migrated to sarif_get/sarif_triage flow: {action}" }),
            _ => JsonSerializer.Serialize(new { success = false, message = $"Unsupported action: {action}" })
        };
    }

    private static async Task<string> RenderStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await SarifTools.SarifGet(includeEvidence: false, limit: 10);
        return RenderDualPurposeText(result);
    }

    private static async Task<string> RenderListAsync(CancellationToken cancellationToken)
    {
        var filter = AnsiConsole.Ask<string>("Filter expression (e.g. severity:high rule:SQLI path:controllers):", string.Empty);
        var includeEvidence = AnsiConsole.Confirm("Include evidence?", false);
        var limit = AnsiConsole.Ask<int>("Limit:", 10);

        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            await SarifTools.SarifFilter(filter);
        }

        var result = await SarifTools.SarifGet(includeEvidence, limit);
        return RenderDualPurposeText(result);
    }

    private static async Task<string> RenderTriageAsync(CancellationToken cancellationToken)
    {
        var state = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Triage state")
                .AddChoices("TP", "FP"));
        var reason = AnsiConsole.Ask<string>("Reason:");
        var target = AnsiConsole.Ask<string>("Target (alias like 1/@1/S-01, CSV aliases, raw ID, or scope):", "scope");

        cancellationToken.ThrowIfCancellationRequested();
        var result = await SarifTools.SarifTriage(state, reason, target);
        return RenderDualPurposeText(result);
    }

    private static string RenderDualPurposeText(CallToolResult result)
    {
        var text = (result.Content.FirstOrDefault() as TextContentBlock)?.Text ?? string.Empty;
        var (displayMarkdown, hiddenJsonState) = SplitDualPurposeText(text);

        if (!string.IsNullOrWhiteSpace(hiddenJsonState))
        {
            RenderStatePanel(hiddenJsonState);
        }

        if (!string.IsNullOrWhiteSpace(displayMarkdown))
        {
            AnsiConsole.Write(new Text(displayMarkdown));
        }

        return text;
    }

    private static (string displayMarkdown, string hiddenJsonState) SplitDualPurposeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (string.Empty, string.Empty);
        }

        var delimiter = SarifTools.StateContextDelimiter;
        var delimiterIndex = text.IndexOf(delimiter, StringComparison.Ordinal);
        if (delimiterIndex < 0)
        {
            return (ExtractVulnerabilityReport(text), string.Empty);
        }

        var displayMarkdown = ExtractVulnerabilityReport(text[..delimiterIndex]);
        var hiddenJsonState = text[(delimiterIndex + delimiter.Length)..]
            .TrimStart('\r', '\n')
            .Trim();
        return (displayMarkdown, hiddenJsonState);
    }

    private static string ExtractVulnerabilityReport(string text)
    {
        const string openingTag = "<vulnerability_report>";
        const string closingTag = "</vulnerability_report>";

        var openingIndex = text.IndexOf(openingTag, StringComparison.Ordinal);
        if (openingIndex < 0)
        {
            return text.Trim();
        }

        openingIndex += openingTag.Length;
        var closingIndex = text.IndexOf(closingTag, openingIndex, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return text.Trim();
        }

        return text[openingIndex..closingIndex].Trim();
    }

    private static void RenderStatePanel(string hiddenJsonState)
    {
        try
        {
            using var doc = JsonDocument.Parse(hiddenJsonState);
            if (!doc.RootElement.TryGetProperty("context", out var context))
            {
                return;
            }

            var scopeLabel = "global";
            if (context.TryGetProperty("active_scope", out var activeScope)
                && activeScope.ValueKind == JsonValueKind.Object
                && activeScope.EnumerateObject().Any())
            {
                scopeLabel = string.Join(", ", activeScope.EnumerateObject().Select(p => $"{p.Name}={p.Value.ToString()}"));
            }

            var progressLabel = "n/a";
            if (context.TryGetProperty("metrics", out var metrics)
                && metrics.TryGetProperty("returned_in_batch", out var returned)
                && metrics.TryGetProperty("remaining_in_scope", out var remaining))
            {
                progressLabel = $"{returned.GetInt32()} returned / {remaining.GetInt32()} remaining";
            }

            var panel = new Panel($"Scope: {scopeLabel}\nProgress: {progressLabel}")
            {
                Header = new PanelHeader("Active Triage Workspace")
            };

            AnsiConsole.Write(panel);
        }
        catch (JsonException)
        {
            // ignore malformed hidden context in CLI fallback path
        }
    }
}
