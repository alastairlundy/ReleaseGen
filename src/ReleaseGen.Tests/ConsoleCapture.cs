namespace ReleaseGen.Tests;

using Spectre.Console;

/// <summary>
/// Captures the ReleaseGen console surface by swapping the process-wide Spectre console
/// for a StringWriter-backed one for the duration of one test body, restoring the previous
/// console afterwards. Because command-level tests are serialized by <see cref="TestWorkspace"/>,
/// no other test writes through the swapped console mid-capture.
/// </summary>
internal static class ConsoleCapture
{
    /// <summary>
    /// Captures everything the CLI wrote to the console, with whitespace flattened: Spectre
    /// wraps long lines at the console profile width, which would otherwise insert artificial
    /// line breaks inside the sentences tests assert on. The default profile enrichment is
    /// dropped so the capture stays plain text everywhere: on CI it force-enables ANSI after
    /// <see cref="AnsiSupport.No"/> is applied, which would put escape codes around the styled
    /// prefixes into the captured text and break the assertions on those prefixes.
    /// </summary>
    /// <param name="action">The test body that drives the command.</param>
    /// <returns>The captured plain-text console output.</returns>
    public static async Task<string> CaptureAsync(Func<Task> action)
    {
        IAnsiConsole previous = AnsiConsole.Console;
        StringWriter writer = new();

        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });

        try
        {
            await action();
            return string.Join(' ', writer.ToString().Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }
}
