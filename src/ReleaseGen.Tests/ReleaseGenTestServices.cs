namespace ReleaseGen.Tests;

using CliInvoke.Core;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenLib;
using ReleaseGenLib.Abstractions;

/// <summary>
/// The shared container roots behind the ReleaseGen test attributes: the production
/// composition from <c>AddReleaseGen</c> with the test doubles registered after it, so
/// last-registration-wins wires every stage to the recording invoker and the OutputType stub
/// without forking the production wiring. Roots are built once per process and scoped per test
/// by the attributes, which keeps each test's recording invoker private to that test under
/// TUnit's parallel execution.
/// </summary>
internal static class ReleaseGenTestServices
{
    /// <summary>
    /// Builds the root used by <see cref="ReleaseGenDiAttribute"/>: production composition
    /// minus the real process invoker and MSBuild path.
    /// </summary>
    /// <returns>The root provider; the process keeps it for the suite's lifetime.</returns>
    public static ServiceProvider BuildRecordingRoot()
    {
        ServiceCollection services = new();

        services.AddReleaseGen();

        // Registered after AddReleaseGen so these win the last-registration-wins resolution
        // the stage commands and runners observe:
        // - the OutputType stub keeps the executables cross-check off the real toolchain;
        // - the recording invoker is scoped so each test's scope captures its own instance
        //   (a root singleton runner would stay stuck to the first test's invoker);
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
    /// Builds the root used by <see cref="ReleaseGenRealInvokerDiAttribute"/>: the unmodified
    /// production composition, real CliInvoke invoker included, for the integration leg that
    /// pushes to a local folder source.
    /// </summary>
    /// <returns>The root provider; the process keeps it for the suite's lifetime.</returns>
    public static ServiceProvider BuildRealInvokerRoot()
    {
        ServiceCollection services = new();
        services.AddReleaseGen();
        return services.BuildServiceProvider();
    }
}
