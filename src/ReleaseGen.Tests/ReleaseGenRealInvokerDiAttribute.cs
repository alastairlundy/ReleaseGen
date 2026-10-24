namespace ReleaseGen.Tests;

using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

/// <summary>
/// Per-test DI with the real CliInvoke invoker: the unmodified production composition for the
/// integration tests that run a real <c>dotnet</c> process against local sources instead of
/// recording invocations. Scopes are per test, like <see cref="ReleaseGenDiAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReleaseGenRealInvokerDiAttribute : DependencyInjectionDataSourceAttribute<IServiceScope>
{
    private static readonly ServiceProvider Root = ReleaseGenTestServices.BuildRealInvokerRoot();

    /// <inheritdoc />
    public override IServiceScope CreateScope(DataGeneratorMetadata dataGeneratorMetadata) =>
        Root.CreateScope();

    /// <inheritdoc />
    public override object Create(IServiceScope scope, Type type) =>
        type == typeof(IServiceProvider)
            ? scope.ServiceProvider
            : scope.ServiceProvider.GetRequiredService(type);
}
