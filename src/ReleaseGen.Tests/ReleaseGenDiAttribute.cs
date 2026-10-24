namespace ReleaseGen.Tests;

using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

/// <summary>
/// Per-test DI for the ReleaseGen suite: TUnit creates one test-class instance per test, so
/// each test receives its own child scope over the shared recording root. Constructor
/// parameters resolve from that scope - commands take the scope's <see
/// cref="IServiceProvider"/>, and assertions take the scope's own <see
/// cref="RecordingProcessInvoker"/>, whose recorded invocations stay private to the test.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReleaseGenDiAttribute : DependencyInjectionDataSourceAttribute<IServiceScope>
{
    private static readonly ServiceProvider Root = ReleaseGenTestServices.BuildRecordingRoot();

    /// <inheritdoc />
    public override IServiceScope CreateScope(DataGeneratorMetadata dataGeneratorMetadata) =>
        Root.CreateScope();

    /// <inheritdoc />
    public override object Create(IServiceScope scope, Type type) =>
        type == typeof(IServiceProvider)
            ? scope.ServiceProvider
            : scope.ServiceProvider.GetRequiredService(type);
}
