using CliInvoke.Core;
using ReleaseGen.Cli.Helpers;

namespace ReleaseGen.Tests;

/// <summary>
/// The installed-SDK probe behind the publish stage's API-key channel: <c>dotnet nuget
/// push</c> reads <c>NUGET_API_KEY</c> from NuGet 7.6 (the .NET 10.0.300 SDK) onward, so the
/// stage delivers the key through the child's environment there and as <c>--api-key</c> on
/// older SDKs. Every probe that cannot answer - a failed exit, unparseable output, a
/// missing dotnet - falls back to the argument channel, which every SDK accepts.
/// </summary>
public class DotnetSdkTests
{
    [Test]
    public async Task The_10_0_300_sdk_reads_the_environment() =>
        await Assert.That(DotnetSdk.SupportsEnvironmentApiKey("10.0.300")).IsTrue();

    [Test]
    public async Task An_older_sdk_falls_back_to_the_argument_channel() =>
        await Assert.That(DotnetSdk.SupportsEnvironmentApiKey("10.0.100")).IsFalse();

    [Test]
    public async Task A_major_below_the_floor_falls_back() =>
        await Assert.That(DotnetSdk.SupportsEnvironmentApiKey("9.0.300")).IsFalse();

    [Test]
    public async Task A_prerelease_of_the_floor_band_counts() =>
        await Assert.That(DotnetSdk.SupportsEnvironmentApiKey("10.0.300-preview.1.25120.10")).IsTrue();

    [Test]
    public async Task A_newer_prerelease_sdk_counts() =>
        await Assert.That(DotnetSdk.SupportsEnvironmentApiKey("11.0.100-rc.3.26522.148")).IsTrue();

    [Test]
    public async Task Trailing_newline_from_the_probe_output_is_ignored() =>
        await Assert.That(DotnetSdk.SupportsEnvironmentApiKey("10.0.300\r\n")).IsTrue();

    [Test]
    public async Task Unparseable_probe_output_falls_back() =>
        await Assert.That(DotnetSdk.SupportsEnvironmentApiKey("not a version")).IsFalse();

    [Test]
    public async Task A_successful_probe_reports_its_sdk_and_spawns_the_version_flag()
    {
        RecordingProcessInvoker invoker = new()
        {
            OutputFactory = _ => ("10.0.300", string.Empty),
        };

        await Assert.That(
            await DotnetSdk.SupportsEnvironmentApiKeyAsync(invoker, CancellationToken.None)).IsTrue();
        await Assert.That(string.Join(' ', invoker.Invocations[0].ArgumentList)).IsEqualTo("--version");
    }

    [Test]
    public async Task A_failed_probe_exit_falls_back_to_the_argument_channel()
    {
        RecordingProcessInvoker invoker = new()
        {
            OutputFactory = _ => ("10.0.300", string.Empty),
            ExitCodeFactory = _ => 1,
        };

        await Assert.That(
            await DotnetSdk.SupportsEnvironmentApiKeyAsync(invoker, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task A_probe_that_cannot_spawn_falls_back_to_the_argument_channel()
    {
        await Assert.That(
            await DotnetSdk.SupportsEnvironmentApiKeyAsync(new ThrowingInvoker(), CancellationToken.None))
            .IsFalse();
    }

    /// <summary>
    /// An invoker that behaves like a missing <c>dotnet</c>: the spawn itself fails, which
    /// the probe must answer with the argument channel rather than an exception.
    /// </summary>
    private sealed class ThrowingInvoker : IProcessInvoker
    {
        public Task<ProcessResult> ExecuteAsync(
            ProcessConfiguration configuration,
            ProcessExitConfiguration? exitConfiguration,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("dotnet is not installed.");

        public Task<BufferedProcessResult> ExecuteBufferedAsync(
            ProcessConfiguration configuration,
            ProcessExitConfiguration? exitConfiguration,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("dotnet is not installed.");
    }
}
