/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

using Microsoft.Extensions.DependencyInjection;
using ReleaseGen.Cli.Commands;

// One composition root for the CLI and the test suite: ReleaseGen's stage services plus the
// CliInvoke pipeline, all registered TryAdd-style so hosts may override any piece.
ServiceCollection services = new();
services.AddReleaseGen();

#pragma warning disable MA0004
await using ServiceProvider provider = services.BuildServiceProvider();
#pragma warning restore MA0004

RootCommand rootCommand = RootCommandComposer.Compose(provider);

try
{
    return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(true);
}
catch (Exception ex)
{
    // Errors stay visible under --quiet; wording names the failing thing.
    CliOutput.Error(ex.Message);
    return 1;
}
