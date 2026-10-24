using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib;

using System.Linq;
using CliInvoke.Core;
using Abstractions;
using Models;

/// <summary>
/// The build stage runner: runs <c>dotnet publish -c Release -r &lt;rid&gt;</c> once per
/// (target project x configured rid) through CliInvoke 3.1.0, fail-fast on the first failing
/// target/rid with an error naming both plus the exit code, and attaches per-target
/// archives for executables-kind outputs. This type supersedes the legacy
/// <c>ProjectBuilder</c> - the publish-only flags on <c>dotnet build</c> and the inverted
/// ReadyToRun check are retired, not patched.
///
/// <para>
/// Output layout, all under the invocation directory (local-only - nothing here uploads;
/// the only network destination in v1 is the publish stage's NuGet source):
/// <c>release/&lt;rid&gt;/&lt;project-name&gt;/</c> holds one target's publish binaries for that
/// rid, and <c>release/&lt;project-name&gt;-&lt;rid&gt;.zip|.tar.gz</c> sits beside the per-rid
/// folders as that folder's archive. The archive format is the target's declared
/// <c>archive</c> key, defaulting to zip; package-kind-only targets are never archived.
/// </para>
///
/// <para>
/// Publish output reaches the caller only through the request's line sinks - this type never
/// writes to the console - and no publish line is filtered or suppressed, so the SDK's
/// IL3000-family analyzer warnings pass through intact. CliInvoke 3.1.0 exposes no
/// line-level process events, so each publish command's captured output is replayed to the sinks as
/// soon as that publish completes rather than interleaved live; fail-fast still stops the
/// stage at the first failing rid.
/// </para>
/// </summary>
public sealed class BuildStageRunner : IBuildStageRunner
{
    /// <summary>
    /// Generous cap on captured publish output so long builds do not truncate warnings before
    /// they reach the console; <see cref="BufferedProcessResult.WasTruncated"/> still
    /// reports any overflow.
    /// </summary>
    private const long MaxBufferedOutputBytes = 64 * 1024 * 1024;

    private readonly IManifestLoader _manifestLoader;
    private readonly IProcessInvoker _processInvoker;
    private readonly IBuildArchiver _buildArchiver;

    /// <summary>
    /// The build stage runner: runs <c>dotnet publish -c Release -r &lt;rid&gt;</c> once per
    /// (target project x configured rid) through CliInvoke 3.1.0, fail-fast on the first failing
    /// target/rid with an error naming both plus the exit code, and attaches per-target
    /// archives for executables-kind outputs. This type supersedes the legacy
    /// <c>ProjectBuilder</c> - the publish-only flags on <c>dotnet build</c> and the inverted
    /// ReadyToRun check are retired, not patched.
    ///
    /// <para>
    /// Output layout, all under the invocation directory (local-only - nothing here uploads;
    /// the only network destination in v1 is the publish stage's NuGet source):
    /// <c>release/&lt;rid&gt;/&lt;project-name&gt;/</c> holds one target's publish binaries for that
    /// rid, and <c>release/&lt;project-name&gt;-&lt;rid&gt;.zip|.tar.gz</c> sits beside the per-rid
    /// folders as that folder's archive. The archive format is the target's declared
    /// <c>archive</c> key, defaulting to zip; package-kind-only targets are never archived.
    /// </para>
    ///
    /// <para>
    /// Publish output reaches the caller only through the request's line sinks - this type never
    /// writes to the console - and no publish line is filtered or suppressed, so the SDK's
    /// IL3000-family analyzer warnings pass through intact. CliInvoke 3.1.0 exposes no
    /// line-level process events, so each publish command invocation's captured output is replayed to the sinks as
    /// soon as that publish completes rather than interleaved live; fail-fast still stops the
    /// stage at the first failing rid.
    /// </para>
    /// </summary>
    public BuildStageRunner(IManifestLoader manifestLoader, IProcessInvoker processInvoker, IBuildArchiver buildArchiver)
    {
        _manifestLoader = manifestLoader;
        _processInvoker = processInvoker;
        _buildArchiver = buildArchiver;
    }

    /// <inheritdoc />
    /// <exception cref="BuildStageException">Thrown when a publish command invocation exited nonzero, or the output folder is missing when archiving.</exception>
    /// <exception cref="ManifestException">Thrown when a target's project path or archive format fails validation.</exception>
    public async Task RunAsync(BuildStageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Manifest manifest = request.Manifest;
        string[] runtimeIdentifiers = manifest.Build.RuntimeIdentifiers;

        if (manifest.Targets.Count == 0)
        {
            // An expected condition is a named report, not an exception.
            request.WriteErrorLine?.Invoke("build: the manifest declares no targets; nothing to publish.");
            return;
        }

        if (runtimeIdentifiers.Length == 0)
        {
            request.WriteErrorLine?.Invoke(
                "build: the manifest's build section declares no runtimeIdentifiers; nothing to publish.");
            return;
        }

        foreach (ManifestTarget target in manifest.Targets)
        {
            FileInfo projectFile = _manifestLoader.ResolveProjectFile(target.Project, request.ManifestDirectory.FullName);
            string projectName = Path.GetFileNameWithoutExtension(projectFile.Name);
            string archiveFormat = target.Archive ?? ArchiveFormats.Zip;

            foreach (string rid in runtimeIdentifiers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Resolved per (target, rid) as pinned; the inputs (preset, build section,
                // archive format, host OS) are rid-independent, so every rid of a target agrees.
                PublishSwitchSet switches = BuildSwitchResolver.Resolve(
                    request.Preset, manifest.Build, archiveFormat);

                DirectoryInfo outputDirectory =
                    new(Path.Combine(request.OutputRoot.FullName, rid, projectName));
                FileInfo archiveFile = new(Path.Combine(
                    request.OutputRoot.FullName,
                    $"{projectName}-{rid}{(string.Equals(archiveFormat, ArchiveFormats.TarGz, StringComparison.OrdinalIgnoreCase)
                        ? ".tar.gz" : ".zip")}"));

                // Deterministic local output: a previous run's stale folder or archive must not
                // leak into this run's binaries (the release/ tree is ours to manage).
                // Fresh OS-level checks: DirectoryInfo/FileInfo cache their first probe.
                if (Directory.Exists(outputDirectory.FullName))
                {
                    Directory.Delete(outputDirectory.FullName, recursive: true);
                }

                // Remove any stale archive for this (target, rid), regardless of the format a
                // previous run may have used, so switching archive formats never leaves an
                // orphaned file beside the new one.
                string archiveStem = Path.Combine(
                    request.OutputRoot.FullName, $"{projectName}-{rid}");
                foreach (string staleExtension in new[] { ".zip", ".tar.gz" })
                {
                    string stalePath = archiveStem + staleExtension;
                    if (File.Exists(stalePath))
                    {
                        File.Delete(stalePath);
                    }
                }

                List<string> argumentList =
                [
                    "publish",
                    projectFile.FullName,
                    "-c",
                    "Release",
                    "-r",
                    rid,
                    "-o",
                    outputDirectory.FullName,
                    .. switches.ToArguments(),
                ];

                // Streamed args note: makes the resolved switch set for this (target, rid)
                // visible on the console (the evidence surface).
                request.WriteLine?.Invoke($"build: dotnet {string.Join(' ', argumentList)}");

                ProcessConfiguration configuration =
                    new(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", argumentList)
                    {
                        WorkingDirectoryPath = request.ManifestDirectory.FullName,
                    };

                BufferedProcessResult result = await _processInvoker.ExecuteBufferedAsync(
                    configuration,
                    ProcessExitConfiguration.WithMaxBufferedOutputBytes(
                        ProcessExitConfiguration.Default, MaxBufferedOutputBytes),
                    cancellationToken).ConfigureAwait(false);

                await ReplayLines(result.StandardOutput, request.WriteLine, cancellationToken).ConfigureAwait(false);

                if (result.WasTruncated)
                {
                    request.WriteErrorLine?.Invoke(
                        $"build: publish output for target '{target.Project}' with runtime identifier '{rid}' was truncated; some lines did not reach the console.");
                }

                await ReplayLines(result.StandardError, request.WriteErrorLine, cancellationToken).ConfigureAwait(false);

                if (result.ExitCode != 0)
                {
                    // The error style: names the target, the rid, and the exit code.
                    throw new BuildStageException(
                        $"build: dotnet publish failed for target '{target.Project}' with runtime identifier '{rid}' (exit code {result.ExitCode}).");
                }

                // Only executables-kind outputs get archives; package kinds never do,
                // and 'none' leaves the per-rid folder unarchived.
                if (target.Kinds.Contains(ArtifactKinds.Executables, StringComparer.OrdinalIgnoreCase) &&
                    !string.Equals(archiveFormat, ArchiveFormats.None, StringComparison.Ordinal))
                {
                    await _buildArchiver.CreateArchiveAsync(outputDirectory, archiveFile, archiveFormat, cancellationToken)
                        .ConfigureAwait(false);
                    request.WriteLine?.Invoke(
                        $"build: archived '{outputDirectory.FullName}' to '{archiveFile.FullName}' ({archiveFormat}).");
                }
            }
        }

        request.WriteLine?.Invoke(
            $"build: published {manifest.Targets.Count} target(s) x {runtimeIdentifiers.Length} runtime identifier(s) under '{request.OutputRoot.FullName}'.");
    }

    /// <summary>
    /// Replays captured process output to the sink one line at a time, dropping only empty
    /// lines. No content-based filtering: IL3000-family analyzer warnings pass through.
    /// </summary>
    /// <param name="text">The captured stdout or stderr text.</param>
    /// <param name="sink">The line sink; null swallows the lines.</param>
    /// <param name="cancellationToken"></param>
    private static async Task ReplayLines(string? text, Action<string>? sink, CancellationToken cancellationToken)
    {
        if (sink is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        using StringReader reader = new(text);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(true) is { } line)
        {
            if (line.Length > 0)
            {
                sink(line);
            }
        }
    }
}