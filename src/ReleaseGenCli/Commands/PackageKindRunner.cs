using ReleaseGenCli.Exceptions;

namespace ReleaseGenCli.Commands;

using ReleaseGenLib.Models;

/// <summary>
/// Runs one artifact kind's pack invocation for the package stage: a single
/// <c>dotnet pack -c Release -o &lt;output&gt;</c> per declared kind. Only declared kinds are
/// produced and nothing is inferred from the project file: <c>lib-package</c> and
/// <c>tool-package</c> are the same pack shape landing in different output partitions
/// (<c>release/packages/lib|tool/&lt;project-name&gt;</c> under the invocation directory,
/// local-only - nothing here uploads; the only network destination in v1 is the publish
/// stage's NuGet source). Whether a pack yields a dotnet tool is the project's own
/// <c>PackAsTool</c> configuration, not something this stage reads. Any other kind is
/// rejected with an error naming it; the stage command never dispatches the build stage's
/// <c>executables</c> kind here.
///
/// <para>
/// Pack output reaches the caller only through the line sinks - this type never writes to
/// the console - and no pack line is filtered, mirroring the build stage's pass-through.
/// CliInvoke 3.1.0 exposes no line-level process events, so each pack's captured
/// output is replayed to the sinks as soon as that pack completes rather than interleaved
/// live; a nonzero exit fails fast with a <see cref="PackageStageException"/> naming the
/// target, the kind, and the exit code.
/// </para>
/// </summary>
public sealed class PackageKindRunner
{
    private readonly IProcessInvoker _processInvoker;

    /// <summary>
    /// Runs one artefact kind's pack invocation for the package stage: a single
    /// <c>dotnet pack -c Release -o &lt;output&gt;</c> per declared kind. Only declared kinds are
    /// produced and nothing is inferred from the project file: <c>lib-package</c> and
    /// <c>tool-package</c> are the same pack shape landing in different output partitions
    /// (<c>release/packages/lib|tool/&lt;project-name&gt;</c> under the invocation directory,
    /// local-only - nothing here uploads; the only network destination in v1 is the publish
    /// stage's NuGet source). Whether a pack yields a dotnet tool is the project's own
    /// <c>PackAsTool</c> configuration, not something this stage reads. Any other kind is
    /// rejected with an error naming it; the stage command never dispatches the build stage's
    /// <c>executables</c> kind here.
    ///
    /// <para>
    /// Pack output reaches the caller only through the line sinks - this type never writes to
    /// the console - and no pack line is filtered, mirroring the build stage's pass-through.
    /// CliInvoke 3.1.0 exposes no line-level process events, so each pack's captured
    /// output is replayed to the sinks as soon as that pack completes rather than interleaved
    /// live; a nonzero exit fails fast with a <see cref="PackageStageException"/> naming the
    /// target, the kind, and the exit code.
    /// </para>
    /// </summary>
    public PackageKindRunner(IProcessInvoker processInvoker)
    {
        _processInvoker = processInvoker;
    }

    /// <summary>
    /// Runs <c>dotnet pack -c Release -o &lt;output&gt;</c> once for the given project and kind.
    /// </summary>
    /// <param name="projectFile">The project file to pack.</param>
    /// <param name="kind">The declared artifact kind: 'lib-package' or 'tool-package'.</param>
    /// <param name="outputRoot">The package stage's output root (release/packages); the kind and project name partition it.</param>
    /// <param name="writeLine">Optional stdout line sink; the command routes these to stage notes.</param>
    /// <param name="writeErrorLine">Optional stderr line sink; the command routes these to warnings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not a package-stage kind; the message names it.</exception>
    /// <exception cref="PackageStageException">The pack invocation exited nonzero.</exception>
    public async Task RunAsync(
        FileInfo projectFile,
        string kind,
        DirectoryInfo outputRoot,
        Action<string>? writeLine,
        Action<string>? writeErrorLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectFile);
        ArgumentNullException.ThrowIfNull(outputRoot);

        // The declared kind alone picks the output partition; the project file is never
        // inspected to decide what to produce - a tool package is produced by declaring the
        // kind and letting pack emit whatever the project is configured to emit.
        string kindFolder = kind switch
        {
            ArtifactKinds.LibPackage => "lib",
            ArtifactKinds.ToolPackage => "tool",
            _ => throw new ArgumentException(
                $"package: unknown artifact kind '{kind}'; the package stage produces '{ArtifactKinds.LibPackage}' and '{ArtifactKinds.ToolPackage}'.",
                nameof(kind)),
        };

        string projectName = Path.GetFileNameWithoutExtension(projectFile.Name);
        DirectoryInfo outputDirectory = new(Path.Combine(outputRoot.FullName, kindFolder, projectName));

        List<string> argumentList =
        [
            "pack",
            projectFile.FullName,
            "-c",
            "Release",
            "-o",
            outputDirectory.FullName,
        ];

        // Streamed args note: makes the pack invocation for this (target, kind) visible on
        // the console (the evidence surface, mirrored for the package stage).
        writeLine?.Invoke(
            $"package: dotnet {string.Join(' ', argumentList)} (kind '{kind}' for target '{projectName}').");

        ProcessConfiguration configuration =
            new(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", argumentList)
            {
                WorkingDirectoryPath = projectFile.Directory?.FullName ?? Environment.CurrentDirectory,
            };

        BufferedProcessResult result = await _processInvoker.ExecuteBufferedAsync(
            configuration,
            ProcessExitConfiguration.Default,
            cancellationToken).ConfigureAwait(true);

        if (result.WasTruncated)
        {
            writeErrorLine?.Invoke(
                $"package: pack output for target '{projectName}' with kind '{kind}' was truncated; some lines did not reach the console.");
        }

        ReplayLines(result.StandardOutput, writeLine);
        ReplayLines(result.StandardError, writeErrorLine);

        if (result.ExitCode != 0)
        {
            // The error style: names the target, the kind, and the exit code.
            throw new PackageStageException(
                $"package: dotnet pack failed for target '{projectName}' with kind '{kind}' (exit code {result.ExitCode}).");
        }
    }

    /// <summary>
    /// Replays captured process output to the sink one line at a time, dropping only empty
    /// lines. No content-based filtering, mirroring the build stage's replay.
    /// </summary>
    /// <param name="text">The captured stdout or stderr text.</param>
    /// <param name="sink">The line sink; null swallows the lines.</param>
    private static void ReplayLines(string? text, Action<string>? sink)
    {
        if (sink is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        using StringReader reader = new(text);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0)
            {
                sink(line);
            }
        }
    }
}