namespace ReleaseGen.Cli.Helpers;

using System.Globalization;

/// <summary>
/// The installed .NET SDK's readiness to take the NuGet API key from the
/// <c>NUGET_API_KEY</c> environment variable. <c>dotnet nuget push</c> reads the variable
/// from NuGet 7.6 (shipped with the .NET 10.0.300 SDK) onward, which lets the publish stage
/// keep the key off the spawned push's command line; older SDKs accept the key only through
/// <c>--api-key</c>, so the stage falls back to the argument channel there.
/// </summary>
public static class DotnetSdk
{
    /// <summary>
    /// The first SDK version whose NuGet reads <c>NUGET_API_KEY</c>: 10.0.300 ships NuGet 7.6.
    /// </summary>
    private static readonly (int Major, int Minor, int Patch) EnvironmentApiKeyVersion = (10, 0, 300);

    /// <summary>
    /// Whether the SDK the <c>dotnet</c> muxer resolves for this directory - the same SDK
    /// that would run the push, <c>global.json</c> included - understands
    /// <c>NUGET_API_KEY</c>. Any probe failure (no dotnet on PATH, an SDK resolution error,
    /// an unreadable version) answers false: the argument channel works on every SDK, so a
    /// probe that cannot answer never breaks a push.
    /// </summary>
    /// <param name="processInvoker">The invoker that runs the probe.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns><see langword="true"/> when the key can travel through the environment.</returns>
    public static async Task<bool> SupportsEnvironmentApiKeyAsync(
        IProcessInvoker processInvoker,
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessConfiguration probe = new(
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", ["--version"]);

            BufferedProcessResult result = await processInvoker
                .ExecuteBufferedAsync(probe, null, cancellationToken)
                .ConfigureAwait(false);

            return result.ExitCode == 0 && SupportsEnvironmentApiKey(result.StandardOutput);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A probe that cannot run says nothing about the SDK; the caller's argument
            // channel is the safe answer.
            return false;
        }
    }

    /// <summary>
    /// Whether a <c>dotnet --version</c> output names an SDK at or above 10.0.300. The
    /// prerelease suffix is dropped before comparison, so a <c>10.0.300-preview.*</c> build of
    /// the feature band counts and a <c>10.0.100</c> does not; anything unparseable answers
    /// false for the same safety reason as a failed probe.
    /// </summary>
    /// <param name="versionOutput">The probe's stdout.</param>
    /// <returns><see langword="true"/> when the version is new enough for NUGET_API_KEY.</returns>
    public static bool SupportsEnvironmentApiKey(string versionOutput)
    {
        string version = versionOutput.Trim();
        int suffix = version.IndexOf('-');

        if (suffix >= 0)
        {
            version = version[..suffix];
        }

        string[] parts = version.Split('.');

        if (parts.Length < 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return false;
        }

        return (major, minor, patch).CompareTo(EnvironmentApiKeyVersion) >= 0;
    }
}
