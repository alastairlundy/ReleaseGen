/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

namespace ReleaseGen.Cli.Helpers;

/// <summary>
/// Spectre-styled console output with the v1 verbosity rules: default output is styled
/// with stage notes, warnings, and errors; <c>--quiet</c> suppresses notes and progress while
/// warnings and errors stay visible; <c>--verbose</c> surfaces each spawned command line, its
/// raw output, and a cause line, and drops the hint that names it. The two flags contradict
/// each other, so applying both to one invocation reports the conflict instead of picking a
/// winner.
/// </summary>
public static class CliOutput
{
    /// <summary>
    /// The escape hatch every default error names, in full. It is the hint line a failure
    /// path gains so a user who just failed can find the mode that shows the detail.
    /// </summary>
    public const string VerboseHint = "run with --verbose for full detail";

    /// <summary>
    /// Gets or sets whether stage notes and progress are suppressed. Warnings and errors are never suppressed.
    /// </summary>
    public static bool Quiet { get; set; }

    /// <summary>
    /// Gets or sets whether this invocation asked for full detail: spawned command lines,
    /// raw child output, and cause lines print, and errors stop carrying
    /// <see cref="VerboseHint"/> (the user is already in full detail).
    /// </summary>
    public static bool Verbose { get; set; }

    /// <summary>
    /// Applies the shared <c>--quiet</c> option's value from a parse result. Commands without
    /// a <c>--verbose</c> option (init) use this and therefore run non-verbose.
    /// </summary>
    /// <param name="parseResult">The parse result of the command being run.</param>
    public static void ApplyQuiet(ParseResult parseResult)
    {
        Quiet = parseResult.GetValue(CliOptions.Quiet);
        Verbose = false;
    }

    /// <summary>
    /// Applies both output modes of a stage command or the chained run: <c>--quiet</c> from
    /// the shared recursive option and <c>--verbose</c> from the command's own option. When
    /// both are set, reports the conflict as the one-line error carrying the diagnostics tip
    /// and returns false, so the caller exits 1 instead of silently choosing an output mode.
    /// </summary>
    /// <param name="parseResult">The parse result of the command being run.</param>
    /// <param name="verboseOption">The command's own <c>--verbose</c> option.</param>
    /// <returns>False when the two flags conflict (the error is already printed); true otherwise.</returns>
    public static bool ApplyOutputModes(ParseResult parseResult, Option<bool> verboseOption)
    {
        Quiet = parseResult.GetValue(CliOptions.Quiet);
        Verbose = parseResult.GetValue(verboseOption);

        if (Quiet && Verbose)
        {
            // One line, both halves: the conflict is named and the diagnostics tip every
            // failure path carries is named with it, so no output mode stays ambiguous.
            ErrorWithoutHint(
                "--quiet and --verbose conflict; pass only one of them. Run with --verbose for full detail.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes a stage note. Suppressed under <c>--quiet</c>.
    /// </summary>
    /// <param name="message">The note text. Should name the stage and what it did.</param>
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
    /// Writes an error. Always visible, even under <c>--quiet</c>. Unless the invocation runs
    /// under <c>--verbose</c>, it is followed by the one hint line naming the escape hatch, so
    /// every failure path points at the mode that shows the full detail.
    /// </summary>
    /// <param name="message">The error text; names the failing thing.</param>
    public static void Error(string message)
    {
        WriteError(message);

        if (!Verbose)
        {
            AnsiConsole.MarkupLine($"[grey]hint:[/] {Markup.Escape(VerboseHint)}");
        }
    }

    /// <summary>
    /// Writes an error with no hint line: the conflict report (which carries the tip itself)
    /// and the commands that register no <c>--verbose</c> option (init), whose errors already
    /// name their own next step instead of a flag they do not accept.
    /// </summary>
    /// <param name="message">The error text; names the failing thing.</param>
    public static void ErrorWithoutHint(string message) => WriteError(message);

    /// <summary>
    /// Writes a cause line: a planned command's diagnosis of what its execution means. It
    /// prints only under <c>--verbose</c>, and only a real run can reach it - the runner never
    /// invokes a record's diagnosis when nothing has been verified.
    /// </summary>
    /// <param name="message">The cause text; names the command's meaning for this run.</param>
    public static void Cause(string message)
    {
        if (!Verbose)
        {
            return;
        }

        AnsiConsole.MarkupLine($"[grey]cause:[/] {Markup.Escape(message)}");
    }

    /// <summary>
    /// Writes the error line itself: the red <c>error:</c> prefix plus the escaped message,
    /// shared by <see cref="Error"/> and <see cref="ErrorWithoutHint"/>.
    /// </summary>
    /// <param name="message">The error text.</param>
    private static void WriteError(string message) =>
        AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(message)}");
}
