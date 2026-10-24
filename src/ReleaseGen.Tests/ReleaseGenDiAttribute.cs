namespace ReleaseGen.Tests;

using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

/// <summary>
/// Per-test DI for the ReleaseGen suite: TUnit creates one test-class instance per test, so
/// each test receives its own container root built by <see
/// cref="ReleaseGenTestServices.BuildRecordingRoot"/>. Constructor parameters resolve from that
/// root - commands ask for the root's own <see cref="ServiceProvider"/>, and assertions take the
/// root's <see cref="RecordingProcessInvoker"/>, whose recorded invocations stay private to the
/// test. The root is disposable, so TUnit disposes it when the test is disposed.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReleaseGenDiAttribute : DependencyInjectionDataSourceAttribute<ServiceProvider>
{
    /// <inheritdoc />
    /// <remarks>
    /// Named CreateScope after TUnit's abstract API, but the value handed to tests is a
    /// container root rather than a child scope: a <see cref="ServiceProvider"/> cannot be
    /// produced from a scope, and a per-test root keeps every resolved singleton - the recording
    /// invoker among them - private to its test under TUnit's parallel execution.
    /// </remarks>
    public override ServiceProvider CreateScope(DataGeneratorMetadata dataGeneratorMetadata) =>
        ReleaseGenTestServices.BuildRecordingRoot();

    /// <inheritdoc />
    public override object Create(ServiceProvider provider, Type type) =>
        type == typeof(ServiceProvider) || type == typeof(IServiceProvider)
            ? provider
            : provider.GetRequiredService(type);
}
