using Spectre.Console;

public static class Logger
{
    public static void LogInfo(string message)
    {
        AnsiConsole.MarkupLine($"[green][[INFO]][/] {message.EscapeMarkup()}");
    }

    public static void LogWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow][[WARN]][/] {message.EscapeMarkup()}");
    }

    public static void LogError(string message)
    {
        AnsiConsole.MarkupLine($"[red][[ERROR]][/] {message.EscapeMarkup()}");
    }

    public static async Task LogProgressAsync(string message)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync(message.EscapeMarkup(), async ctx =>
            {
                await Task.CompletedTask;
            });
    }

    public static async Task StopProgressAsync()
    {
        await Task.CompletedTask;
    }
}