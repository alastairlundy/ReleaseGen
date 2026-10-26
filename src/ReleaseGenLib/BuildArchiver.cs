using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib;

using System.ComponentModel;
using System.Formats.Tar;
using System.IO.Compression;
using Models;

/// <summary>
/// Creates the per-(target, rid) release archives: zip and tar.gz by default per the rid class
/// via System.IO.Compression.ZipFile and System.Formats.Tar plus GZipStream, tar.xz as an
/// explicit opt-in whose compression is the system <c>xz</c> binary rather than a managed
/// stream, and none to leave the per-rid folder unarchived. Archives are written beside
/// the per-rid folder under the local output root and are never uploaded - the only network
/// destination in v1 is the publish stage's NuGet source. The tar and gzip APIs are in-box for
/// net10.0, so no package is added; tar.xz adds no package either, only a dependency on the
/// machine's toolchain.
/// </summary>
public class BuildArchiver : IBuildArchiver
{
    /// <summary>
    /// The compression binary tar.xz hands its tar stream to, resolved on PATH per run. The
    /// name is overridable so a test can prove the environment-not-tooled error names the
    /// missing tool without depending on what this machine has installed.
    /// </summary>
    private const string DefaultXzBinary = "xz";

    /// <summary>
    /// How the <c>xz</c> child runs: no timeout (a compression is bounded by its input, and a
    /// stage must not be killed mid-archive by a default), a graceful kill on cancellation, and
    /// a throwing cancellation so an aborted stage never reads as a compression failure.
    /// </summary>
    private static readonly ProcessExitConfiguration XzExitConfiguration = new(
        ProcessTimeoutPolicy.None,
        ProcessExitBehaviour.GracefulExit,
        ProcessExceptionBehaviour.AllowExceptionsIfUnexpected,
        cancellationThrowsException: true);

    private readonly IProcessInvoker _processInvoker;
    private readonly string _xzBinary;

    /// <summary>
    /// Creates the archiver.
    /// </summary>
    /// <param name="processInvoker">The CliInvoke invoker behind the <c>xz</c> step.</param>
    /// <param name="xzBinary">The compression binary tar.xz runs; defaults to <c>xz</c> on PATH.</param>
    public BuildArchiver(IProcessInvoker processInvoker, string xzBinary = DefaultXzBinary)
    {
        ArgumentNullException.ThrowIfNull(processInvoker);

        _processInvoker = processInvoker;
        _xzBinary = string.IsNullOrWhiteSpace(xzBinary) ? DefaultXzBinary : xzBinary;
    }

    /// <summary>
    /// Archives <paramref name="sourceDirectory"/>'s contents into <paramref name="archiveFile"/>
    /// in the given format. The archive format <see cref="ArchiveFormats.None"/> writes nothing.
    /// An existing archive file is replaced.
    /// </summary>
    /// <param name="sourceDirectory">The per-rid output folder to archive (its project-name folder under the rid folder).</param>
    /// <param name="archiveFile">The archive file to create beside the per-rid folder.</param>
    /// <param name="format">One of <see cref="ArchiveFormats.All"/>.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="BuildStageException">The directory to archive does not exist, or the tar.xz step failed - most often the environment-not-tooled error naming a missing <c>xz</c> binary.</exception>
    /// <exception cref="ManifestException"><paramref name="format"/> is not a known archive format.</exception>
    public async Task CreateArchiveAsync(DirectoryInfo sourceDirectory, FileInfo archiveFile, string format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(archiveFile);

        if (!EnsureWritableFormat(format, archiveFile))
        {
            return;
        }

        // Fresh OS-level checks: DirectoryInfo/FileInfo cache their state on first access, and
        // the runner queried these paths before dotnet publish created them (stale-cache guard).
        if (!Directory.Exists(sourceDirectory.FullName))
        {
            throw new BuildStageException(
                $"build: cannot create archive '{archiveFile.FullName}': output folder '{sourceDirectory.FullName}' does not exist.");
        }

        if (File.Exists(archiveFile.FullName))
        {
            File.Delete(archiveFile.FullName);
        }

        DirectoryInfo? parent = archiveFile.Directory;

        if (parent is not null && !Directory.Exists(parent.FullName))
        {
            parent.Create();
        }

        if (string.Equals(format, ArchiveFormats.Zip, StringComparison.OrdinalIgnoreCase))
        {
            // includeBaseDirectory: false - the archive is named <project-name>-<rid>.zip, so the
            // per-rid project folder itself does not need to wrap the entries.
            await ZipFile.CreateFromDirectoryAsync(
                    sourceDirectory.FullName, archiveFile.FullName, CompressionLevel.Optimal, includeBaseDirectory: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else if (string.Equals(format, ArchiveFormats.TarXz, StringComparison.OrdinalIgnoreCase))
        {
            await CreateTarXz(sourceDirectory, archiveFile, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await CreateTarGz(sourceDirectory, archiveFile, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Confirms <paramref name="format"/> is one the archiver writes, so an unknown value fails
    /// naming the bad value and the allowed set. Every other known format - zip, tar.gz, and
    /// tar.xz, which the system <c>xz</c> binary writes - produces an archive; only
    /// <see cref="ArchiveFormats.None"/> writes nothing. The build stage already skips tar.xz
    /// per Windows-class and any-* rid, so only a Unix-based rid reaches that branch.
    /// </summary>
    /// <param name="format">One of <see cref="ArchiveFormats.All"/>.</param>
    /// <param name="archiveFile">The archive file the error message names.</param>
    /// <returns><see langword="true"/> when the format writes an archive; <see langword="false"/> for <see cref="ArchiveFormats.None"/>.</returns>
    /// <exception cref="ManifestException"><paramref name="format"/> is not a known archive format.</exception>
    private static bool EnsureWritableFormat(string format, FileInfo archiveFile)
    {
        switch (format)
        {
            case ArchiveFormats.None:
                return false;

            case ArchiveFormats.Zip:
            case ArchiveFormats.TarGz:
            case ArchiveFormats.TarXz:
                return true;

            default:
                // The error style: names the bad value and the allowed set.
                throw new ManifestException(
                    $"Unknown archive format '{format}'. Known formats: {string.Join(", ", ArchiveFormats.All)}.");
        }
    }

    /// <summary>
    /// Writes the source folder's file tree as an xz-compressed tar archive by handing the tar
    /// stream to the system <c>xz</c> binary: the tar is staged beside the target archive, xz
    /// compresses it to the staged <c>.xz</c> file, and that file becomes the archive. The
    /// compression is xz's, never a managed stream, so a machine without xz on PATH fails with
    /// an environment-not-tooled error naming the binary instead of silently shipping another
    /// format. Staging never outlives the call: the finally block clears the staged tar and any
    /// product a failed run left behind.
    /// </summary>
    /// <param name="sourceDirectory">The per-rid output folder to archive.</param>
    /// <param name="archiveFile">The .tar.xz file to create.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="BuildStageException"><c>xz</c> is missing from PATH, exited nonzero, or wrote no output.</exception>
    private async Task CreateTarXz(DirectoryInfo sourceDirectory, FileInfo archiveFile, CancellationToken cancellationToken)
    {
        // Staged beside the archive so the final move stays on one volume; the doubled suffix
        // can never be mistaken for a real artifact ("<project>-<rid>.tar.xz.tar.xz").
        string stagedTar = archiveFile.FullName + ".tar";
        string stagedXz = stagedTar + ".xz";

        try
        {
            await WriteTar(stagedTar, sourceDirectory, cancellationToken).ConfigureAwait(false);
            await CompressWithSystemXz(stagedTar, stagedXz, archiveFile, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteIfPresent(stagedTar);
            DeleteIfPresent(stagedXz);
        }
    }

    /// <summary>
    /// Runs the system <c>xz</c> over the staged tar and installs its product as the archive.
    /// The binary is spawned like every other external command the stages run: its exit code is
    /// surfaced, and a binary the machine does not have fails as environment-not-tooled naming
    /// the missing tool rather than as an archive-format problem.
    /// </summary>
    /// <param name="stagedTar">The staged .tar file xz compresses in place.</param>
    /// <param name="stagedXz">The .xz file xz writes beside the staged tar.</param>
    /// <param name="archiveFile">The archive file the errors name.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="BuildStageException"><c>xz</c> is missing from PATH or cannot be launched, exited nonzero, or wrote no output.</exception>
    private async Task CompressWithSystemXz(
        string stagedTar,
        string stagedXz,
        FileInfo archiveFile,
        CancellationToken cancellationToken)
    {
        DeleteIfPresent(stagedXz);

        BufferedProcessResult result;

        try
        {
            result = await _processInvoker.ExecuteBufferedAsync(
                    new ProcessConfiguration(_xzBinary, [stagedTar]),
                    XzExitConfiguration,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception)
        {
            // Launch failure: the message names the binary, carries the OS's own detail so a
            // permission problem does not read as "not installed", and keeps the install hint
            // in the archiver's usual error style.
            throw new BuildStageException(
                $"build: cannot create archive '{archiveFile.FullName}': the '{_xzBinary}' binary was not found on PATH or could not be launched ({ex.Message}). Install xz (for example 'apt-get install xz-utils', 'brew install xz', or 'pacman -S xz') to produce '{ArchiveFormats.TarXz}' archives, or declare '{ArchiveFormats.Zip}' or '{ArchiveFormats.TarGz}' instead.");
        }

        if (result.ExitCode != 0)
        {
            // The error style: names the archive, the tool, and the exit code, plus the first
            // line of xz's own diagnostics when it offered one.
            throw new BuildStageException(
                $"build: cannot create archive '{archiveFile.FullName}': '{_xzBinary}' exited with code {result.ExitCode}{ErrorDetail(result.StandardError)}.");
        }

        if (!File.Exists(stagedXz))
        {
            throw new BuildStageException(
                $"build: cannot create archive '{archiveFile.FullName}': '{_xzBinary}' reported success but wrote no output at '{stagedXz}'.");
        }

        File.Move(stagedXz, archiveFile.FullName, overwrite: true);
    }

    /// <summary>
    /// Writes the source folder's file tree as a gzip-compressed tar archive, with entry names
    /// relative to the source folder.
    /// </summary>
    /// <param name="sourceDirectory">The per-rid output folder to archive.</param>
    /// <param name="archiveFile">The .tar.gz file to create.</param>
    /// <param name="cancellationToken"></param>
    private async Task CreateTarGz(DirectoryInfo sourceDirectory, FileInfo archiveFile, CancellationToken cancellationToken)
    {
#pragma warning disable MA0004
        await using FileStream fileStream = new(archiveFile.FullName, FileMode.Create, FileAccess.Write, FileShare.None);
        await using GZipStream gzipStream = new(fileStream, CompressionLevel.Optimal, false);
        await using TarWriter writer = new(gzipStream, false);
#pragma warning restore MA0004

        await WriteTarEntries(writer, sourceDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the raw tar to <paramref name="stagedTar"/>: the unmanaged-compression half of the
    /// tar.xz path, where the tar bytes are staged for the system <c>xz</c> binary to compress.
    /// </summary>
    /// <param name="stagedTar">The staged .tar file to write.</param>
    /// <param name="sourceDirectory">The per-rid output folder to archive.</param>
    /// <param name="cancellationToken"></param>
    private static async Task WriteTar(string stagedTar, DirectoryInfo sourceDirectory, CancellationToken cancellationToken)
    {
#pragma warning disable MA0004
        await using FileStream fileStream = new(stagedTar, FileMode.Create, FileAccess.Write, FileShare.None);
        await using TarWriter writer = new(fileStream, false);
#pragma warning restore MA0004

        await WriteTarEntries(writer, sourceDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes every file under the source folder into the open tar, one entry per file with its
    /// name relative to the source folder. Shared by the gzip and xz paths so both formats
    /// archive the identical tree.
    /// </summary>
    /// <param name="writer">The open tar writer.</param>
    /// <param name="sourceDirectory">The per-rid output folder to archive.</param>
    /// <param name="cancellationToken"></param>
    private static async Task WriteTarEntries(
        TarWriter writer,
        DirectoryInfo sourceDirectory,
        CancellationToken cancellationToken)
    {
        foreach (string file in Directory.EnumerateFiles(sourceDirectory.FullName, "*", SearchOption.AllDirectories))
        {
            string entryName = Path.GetRelativePath(sourceDirectory.FullName, file).Replace('\\', '/');
            await writer.WriteEntryAsync(file, entryName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Renders the first non-empty line of a child process's captured stderr for an error
    /// message, or an empty string when it captured nothing.
    /// </summary>
    /// <param name="standardError">The captured stderr text.</param>
    /// <returns>The detail suffix, already prefixed with a colon and space, or empty.</returns>
    private static string ErrorDetail(string? standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return string.Empty;
        }

        using StringReader reader = new(standardError);

        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Length > 0)
            {
                return $": {line.Trim()}";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Deletes a staging leftover if one is present; a missing file is the normal state, and a
    /// cleanup failure never masks the failure the caller is already reporting.
    /// </summary>
    /// <param name="path">The staging path to clear.</param>
    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
