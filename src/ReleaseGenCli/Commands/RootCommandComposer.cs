using Microsoft.Extensions.DependencyInjection;

namespace ReleaseGenCli.Commands;

using System.CommandLine;
using System.CommandLine.Help;

/// <summary>
/// Composes the ReleaseGen root command: a help-dispensing root with one command per
/// stage plus the chained run, wired by hand against the DI container.
/// </summary>
public static class RootCommandComposer
{
    public static RootCommand Compose(ServiceProvider services)
    {
        RootCommand rootCommand = new(
            "Opinionated .NET CLI for building, packaging, and publishing .NET projects.");

        IProcessInvoker processInvoker = services.GetRequiredService<IProcessInvoker>();
        IManifestLoader manifestLoader = services.GetRequiredService<IManifestLoader>();
        IBuildStageRunner buildStageRunner = services.GetRequiredService<IBuildStageRunner>();
        
        rootCommand.Add(new InitCommand());
        rootCommand.Add(new BuildCommand(manifestLoader, buildStageRunner));
        rootCommand.Add(new TestCommand(manifestLoader, processInvoker));
        rootCommand.Add(new PackageCommand(manifestLoader, processInvoker));
        rootCommand.Add(new PublishCommand(manifestLoader, processInvoker));
        rootCommand.Add(new RunCommand(manifestLoader, processInvoker, buildStageRunner));
        rootCommand.Add(CliOptions.Quiet);

        // Bare `releasegen` prints command help; no stage runs without an explicit verb.
        rootCommand.SetAction(parseResult => new HelpAction().Invoke(parseResult));

        return rootCommand;
    }
}
