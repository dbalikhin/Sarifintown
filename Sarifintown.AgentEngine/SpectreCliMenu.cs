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
            "Triage inspect" or "Triage bulk" => JsonSerializer.Serialize(new { success = false, message = $"Action migrated to sarif.get/sarif.triage flow: {action}" }),
            _ => JsonSerializer.Serialize(new { success = false, message = $"Unsupported action: {action}" })
        };
    }

    private static async Task<string> RenderStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await SarifTools.SarifGet(scope: "keep", filter: string.Empty, includeEvidence: false, limit: 10);
        return RenderDualPurposeText(result);
    }

    private static async Task<string> RenderListAsync(CancellationToken cancellationToken)
    {
        var scope = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Scope action")
                .AddChoices("keep", "set", "refine", "clear"));
        var filter = AnsiConsole.Ask<string>("Filter expression (e.g. severity:high, rule:SQLI):", string.Empty);
        var includeEvidence = AnsiConsole.Confirm("Include evidence?", false);
        var limit = AnsiConsole.Ask<int>("Limit:", 10);

        cancellationToken.ThrowIfCancellationRequested();
        var result = await SarifTools.SarifGet(scope, filter, includeEvidence, limit);
        return RenderDualPurposeText(result);
    }

    private static async Task<string> RenderTriageAsync(CancellationToken cancellationToken)
    {
        var state = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Triage state")
                .AddChoices("TP", "FP"));
        var reason = AnsiConsole.Ask<string>("Reason:");
        var target = AnsiConsole.Ask<string>("Target (finding id, id1,id2, or scope):", "scope");

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
        var marker = $"\n\n{delimiter}\n";
        var delimiterIndex = text.IndexOf(marker, StringComparison.Ordinal);
        if (delimiterIndex < 0)
        {
            return (text, string.Empty);
        }

        var displayMarkdown = text[..delimiterIndex].Trim();
        var hiddenJsonState = text[(delimiterIndex + marker.Length)..].Trim();
        return (displayMarkdown, hiddenJsonState);
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
