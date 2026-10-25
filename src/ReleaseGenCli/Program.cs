using Microsoft.Extensions.DependencyInjection;
using ReleaseGenCli.Commands;

// One composition root for the CLI and the test suite: ReleaseGen's stage services plus the
// CliInvoke pipeline, all registered TryAdd-style so hosts may override any piece.
ServiceCollection services = new();
services.AddReleaseGen();

using ServiceProvider provider = services.BuildServiceProvider();

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
