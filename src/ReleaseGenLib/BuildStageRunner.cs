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
/// rid, and <c>release/&lt;project-name&gt;-&lt;rid&gt;.zip|.tar.gz|.tar.xz</c> sits beside the per-rid
/// folders as that folder's archive. The archive formats are the target's declared
/// <c>archiveFormats</c> key - one archive per declared format per (target, rid) - and when
/// the key is absent they resolve per rid class: zip for Windows-class and portable
/// <c>any-*</c> rids, zip plus tar.gz for Unix-based rids (tar.xz is never a default).
/// Package-kind-only targets are never archived.
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
    private readonly PlannedCommandRunner _plannedCommandRunner;
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
    /// rid, and <c>release/&lt;project-name&gt;-&lt;rid&gt;.zip|.tar.gz|.tar.xz</c> sits beside the per-rid
    /// folders as that folder's archive. The archive formats are the target's declared
    /// <c>archiveFormats</c> key - one archive per declared format per (target, rid), defaulting
    /// to the rid-class set when the key is absent; package-kind-only targets are never archived.
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
        _buildArchiver = buildArchiver;
        _plannedCommandRunner = new PlannedCommandRunner(processInvoker)
        {
            ExitConfiguration = ProcessExitConfiguration.WithMaxBufferedOutputBytes(
                ProcessExitConfiguration.Default, MaxBufferedOutputBytes),
        };
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

            foreach (string rid in runtimeIdentifiers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Absent key resolves per rid class (archiving stays opt-out): zip alone for
                // Windows-class and portable any-* rids, zip plus tar.gz for Unix-based rids -
                // tar.xz is never a default. A declared key overrides the defaults entirely,
                // and every resolved format produces one archive per (target, rid).
                string[] archiveFormats =
                    target.ArchiveFormats.Length > 0 ? target.ArchiveFormats : ArchiveFormats.DefaultsFor(rid);

                // Resolved per (target, rid) as pinned; the inputs (preset, build section,
                // resolved archive formats, host OS) agree for every rid of a target with a
                // declared key, and the rid-dependent default only ever adds zip or tar.gz.
                PublishSwitchSet switches = BuildSwitchResolver.Resolve(
                    request.Preset, manifest.Build, SwitchBasisFormat(archiveFormats));

                DirectoryInfo outputDirectory =
                    new(Path.Combine(request.OutputRoot.FullName, rid, projectName));

                // Deterministic local output: a previous run's stale folder or archive must not
                // leak into this run's binaries (the release/ tree is ours to manage).
                // Writes only in a real run - the dry-run report leaves the tree untouched.
                // Fresh OS-level checks: DirectoryInfo/FileInfo cache their first probe.
                if (request.RunMode == StageRunMode.Execute)
                {
                    if (Directory.Exists(outputDirectory.FullName))
                    {
                        Directory.Delete(outputDirectory.FullName, recursive: true);
                    }

                    // Remove any stale archive for this (target, rid), regardless of the format
                    // a previous run may have used, so switching archive formats never leaves an
                    // orphaned file beside the new one.
                    string archiveStem = Path.Combine(
                        request.OutputRoot.FullName, $"{projectName}-{rid}");
                    foreach (string staleExtension in new[] { ".zip", ".tar.gz", ".tar.xz" })
                    {
                        string stalePath = archiveStem + staleExtension;
                        if (File.Exists(stalePath))
                        {
                            File.Delete(stalePath);
                        }
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

                // The planned record is the whole story of this publish: the streamed-args line
                // (the evidence surface) both printed modes show, and the process the runner
                // executes only in a real run.
                PlannedCommand publishCommand = new()
                {
                    CommandLine = $"build: dotnet {string.Join(' ', argumentList)}",
                    TargetFilePath = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
                    ArgumentList = argumentList,
                    WorkingDirectory = request.ManifestDirectory.FullName,
                    ArtifactPaths = [outputDirectory.FullName],
                };

                BufferedProcessResult? result = await _plannedCommandRunner.RunAsync(
                    publishCommand, request.RunMode, request.WriteLine, cancellationToken)
                    .ConfigureAwait(false);

                // The dry-run report printed the plan; nothing ran, so there is no result to
                // replay or fail on.
                if (result is not null)
                {
                    await ReplayPublishResultAsync(result, request, target, rid, cancellationToken)
                        .ConfigureAwait(false);
                }

                // Only executables-kind outputs get archives; package kinds never do,
                // and 'none' leaves the per-rid folder unarchived. Each resolved format
                // writes its own archive beside the per-rid folder, except tar.xz on a
                // Windows-class or portable any-* rid: one warning per skipped (target, rid)
                // pair, naming the target, the rid, the format, and the reason.
                if (target.Kinds.Contains(ArtifactKinds.Executables, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (string archiveFormat in archiveFormats)
                    {
                        if (string.Equals(archiveFormat, ArchiveFormats.None, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (string.Equals(archiveFormat, ArchiveFormats.TarXz, StringComparison.Ordinal) &&
                            !ArchiveFormats.SupportsTarXz(rid))
                        {
                            request.WriteErrorLine?.Invoke(
                                $"build: target '{target.Project}' with runtime identifier '{rid}' skips archive format '{ArchiveFormats.TarXz}': tar.xz cannot be produced for Windows-class or portable (any-*) runtime identifiers.");
                            continue;
                        }

                        FileInfo archiveFile = new(Path.Combine(
                            request.OutputRoot.FullName,
                            $"{projectName}-{rid}{ArchiveExtension(archiveFormat)}"));

                        // The archiver re-checks at run time that the publish output folder
                        // exists; the plan reports that guard as conditional instead of running
                        // it, so the dry-run print describes work a real run could skip.
                        PlannedCommand archiveCommand = new()
                        {
                            CommandLine =
                                $"build: archiving '{outputDirectory.FullName}' to '{archiveFile.FullName}' ({archiveFormat}).",
                            ExecuteAsync = token => _buildArchiver.CreateArchiveAsync(
                                outputDirectory, archiveFile, archiveFormat, token),
                            ArtifactPaths = [archiveFile.FullName],
                            Condition = $"output folder '{outputDirectory.FullName}' exists",
                        };

                        await _plannedCommandRunner.RunAsync(
                            archiveCommand, request.RunMode, request.WriteLine, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        request.WriteLine?.Invoke(
            request.RunMode == StageRunMode.Execute
                ? $"build: published {manifest.Targets.Count} target(s) x {runtimeIdentifiers.Length} runtime identifier(s) under '{request.OutputRoot.FullName}'."
                : $"build: planned {manifest.Targets.Count} target(s) x {runtimeIdentifiers.Length} runtime identifier(s); the dry run wrote nothing.");
    }

    /// <summary>
    /// Replays one executed publish's captured output through the request's sinks and fails the
    /// stage on a nonzero exit, naming the target, the rid, and the exit code.
    /// </summary>
    /// <param name="result">The executed publish's process result.</param>
    /// <param name="request">The build-stage request carrying the line sinks.</param>
    /// <param name="target">The target being published, for the wording.</param>
    /// <param name="rid">The runtime identifier being published, for the wording.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <exception cref="BuildStageException">The publish invocation exited nonzero.</exception>
    private static async Task ReplayPublishResultAsync(
        BufferedProcessResult result,
        BuildStageRequest request,
        ManifestTarget target,
        string rid,
        CancellationToken cancellationToken)
    {
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
    }

    /// <summary>
    /// The archive format switch resolution keys on: the Windows single-file default asks
    /// whether the target is zip-archived, so a declared list counts as zip-archived when it
    /// carries zip anywhere; otherwise the first declared format carries the answer.
    /// </summary>
    /// <param name="archiveFormats">The target's resolved format list (never empty).</param>
    /// <returns>The format the switch resolver should treat as the target's archive format.</returns>
    private static string SwitchBasisFormat(string[] archiveFormats) =>
        archiveFormats.Contains(ArchiveFormats.Zip, StringComparer.Ordinal)
            ? ArchiveFormats.Zip
            : archiveFormats[0];

    /// <summary>
    /// The file extension one archive format writes: zip is the plain '.zip', and the two tar
    /// formats carry their compression suffix ('.tar.gz', '.tar.xz').
    /// </summary>
    /// <param name="archiveFormat">One of <see cref="ArchiveFormats.All"/>.</param>
    /// <returns>The extension, including its leading dot.</returns>
    private static string ArchiveExtension(string archiveFormat) =>
        string.Equals(archiveFormat, ArchiveFormats.TarGz, StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : string.Equals(archiveFormat, ArchiveFormats.TarXz, StringComparison.OrdinalIgnoreCase)
                ? ".tar.xz"
                : ".zip";

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

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length > 0)
            {
                sink(line);
            }
        }
    }
}