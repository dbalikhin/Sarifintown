using Spectre.Console;
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
        TriageWorkflowService workflow,
        string action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return action switch
        {
            "Triage status" => await RenderStatusAsync(workflow, cancellationToken),
            "Triage list" => await RenderListAsync(workflow, cancellationToken),
            "Triage inspect" => await RenderInspectAsync(workflow, cancellationToken),
            "Triage decision" => await RenderTriageAsync(workflow, cancellationToken),
            "Triage bulk" => await RenderTriageBulkAsync(workflow, cancellationToken),
            _ => JsonSerializer.Serialize(new { success = false, message = $"Unsupported action: {action}" })
        };
    }

    private static async Task<string> RenderStatusAsync(TriageWorkflowService workflow, CancellationToken cancellationToken)
    {
        var status = await workflow.GetStatusAsync(cancellationToken);

        AnsiConsole.MarkupLine($"[green]Total findings:[/] {status.TotalFindings}");
        AnsiConsole.MarkupLine($"[yellow]Open:[/] {status.OpenCount}  [blue]Triaged:[/] {status.TriagedCount}  [green]TP:[/] {status.TruePositiveCount}  [red]FP:[/] {status.FalsePositiveCount}");

        return JsonSerializer.Serialize(status);
    }

    private static async Task<string> RenderListAsync(TriageWorkflowService workflow, CancellationToken cancellationToken)
    {
        var severity = AnsiConsole.Ask<string>("Severity filter (optional, CSV):", string.Empty);
        var rule = AnsiConsole.Ask<string>("Rule filter (optional, CSV):", string.Empty);
        var file = AnsiConsole.Ask<string>("File filter (optional, wildcard supported):", string.Empty);
        var state = AnsiConsole.Ask<string>("State filter (optional: Open/TP/FP):", string.Empty);
        var limit = AnsiConsole.Ask<int>("Limit:", 10);

        var results = await workflow.ListAsync(new TriageQueryOptions(severity, rule, file, state, limit), cancellationToken);
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("FindingId");
        table.AddColumn("Rule");
        table.AddColumn("File");
        table.AddColumn("Line");
        table.AddColumn("Severity");
        table.AddColumn("State");
        table.AddColumn("TPS");

        foreach (var item in results)
        {
            table.AddRow(
                item.FindingId,
                item.RuleName,
                item.FilePath,
                item.LineNumber?.ToString() ?? "",
                item.Severity,
                item.State,
                item.PriorityScore.ToString("0.##"));
        }

        AnsiConsole.Write(table);
        return JsonSerializer.Serialize(results);
    }

    private static async Task<string> RenderInspectAsync(TriageWorkflowService workflow, CancellationToken cancellationToken)
    {
        var findingId = AnsiConsole.Ask<string>("Finding ID:");
        var result = await workflow.InspectAsync(findingId, cancellationToken);

        if (result == null)
        {
            var errorPayload = new { success = false, message = $"Finding not found: {findingId}" };
            AnsiConsole.MarkupLine($"[red]{errorPayload.message}[/]");
            return JsonSerializer.Serialize(errorPayload);
        }

        AnsiConsole.MarkupLine($"[green]Rule:[/] {result.RuleId} ({result.RuleName})");
        AnsiConsole.MarkupLine($"[green]Severity:[/] {result.Severity}  [green]State:[/] {result.State}");
        AnsiConsole.MarkupLine($"[green]Flow steps:[/] {result.DataFlowSteps.Count}");

        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> RenderTriageAsync(TriageWorkflowService workflow, CancellationToken cancellationToken)
    {
        var findingId = AnsiConsole.Ask<string>("Finding ID:");
        var state = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Triage state")
                .AddChoices("TP", "FP"));
        var reason = AnsiConsole.Ask<string>("Reason:");
        var author = AnsiConsole.Ask<string>("Author:", "User");

        var result = await workflow.TriageAsync(findingId, state, reason, author, cancellationToken);
        AnsiConsole.MarkupLine(result.Success
            ? $"[green]{result.Message}[/]"
            : $"[red]{result.Message}[/]");

        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> RenderTriageBulkAsync(TriageWorkflowService workflow, CancellationToken cancellationToken)
    {
        var state = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Triage state")
                .AddChoices("TP", "FP"));
        var reason = AnsiConsole.Ask<string>("Reason:");
        var severity = AnsiConsole.Ask<string>("Severity filter (optional, CSV):", string.Empty);
        var rule = AnsiConsole.Ask<string>("Rule filter (optional, CSV):", string.Empty);
        var file = AnsiConsole.Ask<string>("File filter (optional, wildcard supported):", string.Empty);
        var dryRun = AnsiConsole.Confirm("Dry run only?", true);
        var author = AnsiConsole.Ask<string>("Author:", "User");

        var result = await workflow.TriageBulkAsync(
            state,
            reason,
            new TriageQueryOptions(severity, rule, file, string.Empty, int.MaxValue),
            dryRun,
            author,
            cancellationToken);

        AnsiConsole.MarkupLine(result.Success
            ? $"[green]{result.Message}[/]"
            : $"[red]{result.Message}[/]");

        return JsonSerializer.Serialize(result);
    }
}
