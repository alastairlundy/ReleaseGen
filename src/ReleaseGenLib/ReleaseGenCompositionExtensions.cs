namespace ReleaseGenLib;

using CliInvoke.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// The single composition root for the ReleaseGen stage services: the CliInvoke process
/// invocation pipeline, the Microsoft.Build evaluation path, the archive and build-stage
/// runners, and the manifest loader (registered both as the concrete type the stage commands
/// resolve and as <see cref="IManifestLoader"/>). The shipped CLI and the test suite compose
/// through this one method so their containers cannot drift apart; every registration uses
/// TryAdd, so a host may pre-register its own implementations (test doubles, alternate
/// invokers) and wins by registering after this call.
/// </summary>
public static class ReleaseGenCompositionExtensions
{
    /// <summary>
    /// Adds the ReleaseGen stage services to <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to compose into.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddReleaseGen(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Process invocation, middleware, and the CliInvoke pipeline itself.
        services.AddCliInvoke(ServiceLifetime.Singleton);

        // The Microsoft.Build evaluation path behind the manifest's OutputType cross-check.
        services.TryAddSingleton<IProjectInfoProvider, ProjectInfoProvider>();

        // The build stage's archive step and the stage runner consuming both.
        services.TryAddSingleton<IBuildArchiver, BuildArchiver>();
        services.TryAddSingleton<IBuildStageRunner, BuildStageRunner>();

        // The manifest loader: one instance behind both the concrete registration the stage
        // commands resolve and the abstraction library consumers program against.
        services.TryAddSingleton<ManifestLoader>();
        services.TryAddSingleton<IManifestLoader>(static provider =>
            provider.GetRequiredService<ManifestLoader>());

        return services;
    }
}
