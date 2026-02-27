using Spectre.Console;

namespace Sarifintown.AgentEngine;

internal static class SpectreCliMenu
{
    public static string Start()
    {
        try
        {
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Sarifintown CLI Menu[/]")
                    .PageSize(10)
                    .AddChoices(
                    [
                        "List SARIF files",
                        "Load and filter SARIF",
                        "Extract code flow",
                        "Generate analysis report",
                        "Exit"
                    ]));

            return action;
        }
        catch (InvalidOperationException)
        {
            return "non-interactive-terminal";
        }
    }
}
