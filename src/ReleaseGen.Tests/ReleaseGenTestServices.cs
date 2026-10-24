namespace ReleaseGen.Tests;

using CliInvoke.Core;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenLib;
using ReleaseGenLib.Abstractions;

/// <summary>
/// The container roots behind the ReleaseGen test attributes: the production composition from
/// <c>AddReleaseGen</c> with the test doubles registered after it, so last-registration-wins
/// wires every stage to the recording invoker and the OutputType stub without forking the
/// production wiring. The attributes build one root per test, which keeps each test's recording
/// invoker private to that test under TUnit's parallel execution.
/// </summary>
internal static class ReleaseGenTestServices
{
    /// <summary>
    /// Builds a fresh root for one test using <see cref="ReleaseGenDiAttribute"/>: production
    /// composition minus the real process invoker and MSBuild path.
    /// </summary>
    /// <returns>The test's root provider; TUnit disposes it when the test is disposed.</returns>
    public static ServiceProvider BuildRecordingRoot()
    {
        ServiceCollection services = new();

        services.AddReleaseGen();

        // Registered after AddReleaseGen so these win the last-registration-wins resolution
        // the stage commands and runners observe:
        // - the OutputType stub keeps the executables cross-check off the real toolchain;
        // - the recording invoker backs both IProcessInvoker and the assertions' own
        //   RecordingProcessInvoker; the per-test root keeps that instance private to the test;
        // - the stub stays a stateless singleton so the singleton ManifestLoader has no
        //   scoped dependency.
        services.AddSingleton<IProjectInfoProvider>(new StubProjectInfoProvider());
        services.AddScoped<RecordingProcessInvoker>();
        services.AddScoped<IProcessInvoker>(static provider =>
            provider.GetRequiredService<RecordingProcessInvoker>());
        services.AddScoped<IBuildStageRunner, BuildStageRunner>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a fresh root for one test using <see cref="ReleaseGenRealInvokerDiAttribute"/>:
    /// the unmodified production composition, real CliInvoke invoker included, for the
    /// integration leg that pushes to a local folder source.
    /// </summary>
    /// <returns>The test's root provider; TUnit disposes it when the test is disposed.</returns>
    public static ServiceProvider BuildRealInvokerRoot()
    {
        ServiceCollection services = new();
        services.AddReleaseGen();
        return services.BuildServiceProvider();
    }
}
