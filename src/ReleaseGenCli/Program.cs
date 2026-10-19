using System.CommandLine;
using CliInvoke.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenCli.Commands;
using ReleaseGenLib;
using ReleaseGenLib.Abstractions;

ServiceCollection services = new();

// Manual DI hookup per T002: CliInvoke v3 services plus the project provider and build runner.
services.AddCliInvoke(ServiceLifetime.Singleton);
services.AddSingleton<IProjectInfoProvider, ProjectInfoProvider>();
services.AddSingleton<IBuildStageRunner, BuildStageRunner>();
services.AddSingleton<ManifestLoader>(sp => new ManifestLoader(sp.GetRequiredService<IProjectInfoProvider>()));

using ServiceProvider provider = services.BuildServiceProvider();

RootCommand rootCommand = RootCommandComposer.Compose(provider);

try
{
    return await rootCommand.Parse(args).InvokeAsync();
}
catch (Exception ex)
{
    // Errors stay visible under --quiet (T017); wording names the failing thing (D009).
    CliOutput.Error(ex.Message);
    return 1;
}
