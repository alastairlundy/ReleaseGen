// See https://aka.ms/new-console-template for more information

using System.CommandLine;
using CliInvoke.Extensions;
using Microsoft.Extensions.DependencyInjection;

Cli.Ext.ConfigureServices(services =>
{
   services.AddCliInvoke(ServiceLifetime.Singleton);

   services.AddSingleton<IProjectInfoProvider, ProjectInfoProvider>();
   services.AddSingleton<IProjectBuilder, ProjectBuilder>();
});

Cli.Run<RootCommand>(args);