namespace ReleaseGenCli.Helpers;

using System.CommandLine;
using Spectre.Console;

/// <summary>
/// Spectre-styled console output with the v1 verbosity rules (T017): default output is styled
/// with stage notes, warnings, and errors; <c>--quiet</c> suppresses notes and progress while
/// warnings and errors stay visible.
/// </summary>
public static class CliOutput
{
    /// <summary>
    /// Gets or sets whether stage notes and progress are suppressed. Warnings and errors are never suppressed.
    /// </summary>
    public static bool Quiet { get; set; }

    /// <summary>
    /// Applies the shared <c>--quiet</c> option's value from a parse result.
    /// </summary>
    /// <param name="parseResult">The parse result of the command being run.</param>
    public static void ApplyQuiet(ParseResult parseResult) =>
        Quiet = parseResult.GetValue(CliOptions.Quiet);

    /// <summary>
    /// Writes a stage note. Suppressed under <c>--quiet</c>.
    /// </summary>
    /// <param name="message">The note text. Should name the stage and what it did (D009 error style).</param>
    public static void Note(string message)
    {
        if (Quiet)
        {
            return;
        }

        AnsiConsole.MarkupLine($"[grey]note:[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Writes a warning. Always visible, even under <c>--quiet</c>.
    /// </summary>
    /// <param name="message">The warning text.</param>
    public static void Warning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]warning:[/] {Markup.Escape(message)}");

    /// <summary>
    /// Writes an error. Always visible, even under <c>--quiet</c>.
    /// </summary>
    /// <param name="message">The error text; names the failing thing (D009 error style).</param>
    public static void Error(string message) =>
        AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(message)}");
}
