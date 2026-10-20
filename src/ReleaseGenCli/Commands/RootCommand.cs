namespace ReleaseGenCli.Commands;

using System.CommandLine;
using System.CommandLine.Help;
using ReleaseGenLib.Abstractions;
/// <summary>
/// Composes the ReleaseGen root command: a help-dispensing root with one command per
/// stage plus the chained run, wired by hand against the DI container.
/// </summary>
public static class RootCommandComposer
{
    public static RootCommand Compose(IServiceProvider services)
    {
        RootCommand rootCommand = new(
            "Opinionated .NET CLI for building, packaging, and publishing .NET projects.");

        rootCommand.Add(new InitCommand(services));
        rootCommand.Add(new BuildCommand(services));
        rootCommand.Add(new TestCommand(services));
        rootCommand.Add(new PackageCommand(services));
        rootCommand.Add(new PublishCommand(services));
        rootCommand.Add(new RunCommand(services));
        rootCommand.Add(CliOptions.Quiet);

        // Bare `releasegen` prints command help; no stage runs without an explicit verb.
        rootCommand.SetAction(parseResult => new HelpAction().Invoke(parseResult));

        return rootCommand;
    }
}
