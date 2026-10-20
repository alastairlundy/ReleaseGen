namespace ReleaseGen.Tests;

using CliInvoke.Core;
using CliInvoke.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// DI composition for command-level tests, mirroring <c>Program.cs</c> minus the MSBuild
/// evaluation provider: the suite's manifests declare package-kind targets (or rely on the
/// loader without a provider), so the D020 cross-check stays out of stage tests and is covered
/// directly in <see cref="ManifestParsingTests"/> instead.
/// </summary>
internal static class ReleaseGenServices
{
    /// <summary>
    /// Builds a container whose process invocations are recorded rather than executed.
    /// </summary>
    /// <param name="invoker">The recording invoker to serve to every stage.</param>
    /// <returns>The provider; the caller disposes it.</returns>
    public static ServiceProvider Create(RecordingProcessInvoker invoker)
    {
        ServiceCollection services = new();

        services.AddSingleton<IProcessInvoker>(invoker);
        services.AddSingleton(new ManifestLoader());
        services.AddSingleton<IBuildStageRunner>(
            sp => new BuildStageRunner(sp.GetRequiredService<IProcessInvoker>()));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a container with the real CliInvoke invoker, used by the publish integration test
    /// that pushes to a local source instead of nuget.org (T018).
    /// </summary>
    /// <returns>The provider; the caller disposes it.</returns>
    public static ServiceProvider CreateWithRealInvoker()
    {
        ServiceCollection services = new();

        services.AddCliInvoke(ServiceLifetime.Singleton);
        services.AddSingleton(new ManifestLoader());

        return services.BuildServiceProvider();
    }
}
