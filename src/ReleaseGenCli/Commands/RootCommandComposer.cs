/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

using System.CommandLine.Help;
using Microsoft.Extensions.DependencyInjection;

namespace ReleaseGen.Cli.Commands;

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