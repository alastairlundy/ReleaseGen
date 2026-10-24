namespace ReleaseGen.Tests;

using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

/// <summary>
/// Per-test DI with the real CliInvoke invoker: the unmodified production composition for the
/// integration tests that run a real <c>dotnet</c> process against local sources instead of
/// recording invocations. Each test gets its own root, like <see cref="ReleaseGenDiAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReleaseGenRealInvokerDiAttribute : DependencyInjectionDataSourceAttribute<ServiceProvider>
{
    /// <inheritdoc />
    /// <remarks>
    /// Named CreateScope after TUnit's abstract API, but the value handed to tests is a
    /// container root rather than a child scope - see <see cref="ReleaseGenDiAttribute"/>.
    /// </remarks>
    public override ServiceProvider CreateScope(DataGeneratorMetadata dataGeneratorMetadata) =>
        ReleaseGenTestServices.BuildRealInvokerRoot();

    /// <inheritdoc />
    public override object Create(ServiceProvider provider, Type type) =>
        type == typeof(ServiceProvider) || type == typeof(IServiceProvider)
            ? provider
            : provider.GetRequiredService(type);
}
