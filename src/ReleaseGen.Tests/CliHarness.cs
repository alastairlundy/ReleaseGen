namespace ReleaseGen.Tests;

using System.CommandLine;
using ReleaseGenCli;
using ReleaseGenCli.Commands;

/// <summary>
/// Invokes one ReleaseGen command through a throwaway root the same way the shipped CLI does
/// (D003): the command plus the shared recursive <c>--quiet</c> option, parsed and invoked, with
/// the command's exit code returned. Mirrors the chained run's in-process stage invocation so
/// tests exercise the production behavior surface.
/// </summary>
internal static class CliHarness
{
    /// <summary>
    /// Parses and invokes <paramref name="command"/> with <paramref name="arguments"/>.
    /// </summary>
    /// <param name="command">The stage command under test.</param>
    /// <param name="arguments">The argv for the command, for example <c>init --overwrite</c>.</param>
    /// <returns>The command's exit code.</returns>
    public static async Task<int> InvokeAsync(Command command, params string[] arguments)
    {
        RootCommand root = new("ReleaseGen test invocation root.");
        root.Add(command);
        root.Add(CliOptions.Quiet);

        ParseResult parseResult = root.Parse(arguments);
        return await parseResult.InvokeAsync();
    }
}
