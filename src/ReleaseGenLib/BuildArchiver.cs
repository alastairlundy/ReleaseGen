using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib;

using System.Formats.Tar;
using System.IO.Compression;
using Models;

/// <summary>
/// Creates the per-(target, rid) release archives: zip and tar.gz by default per the rid class
/// via System.IO.Compression.ZipFile and System.Formats.Tar plus GZipStream, tar.xz as an
/// explicit opt-in, and none to leave the per-rid folder unarchived. Archives are written beside
/// the per-rid folder under the local output root and are never uploaded - the only network
/// destination in v1 is the publish stage's NuGet source. Both tar.gz APIs are in-box for
/// net10.0, so no package is added.
/// </summary>
public class BuildArchiver : IBuildArchiver
{
    /// <summary>
    /// Archives <paramref name="sourceDirectory"/>'s contents into <paramref name="archiveFile"/>
    /// in the given format. The archive format <see cref="ArchiveFormats.None"/> writes nothing.
    /// An existing archive file is replaced.
    /// </summary>
    /// <param name="sourceDirectory">The per-rid output folder to archive (its project-name folder under the rid folder).</param>
    /// <param name="archiveFile">The archive file to create beside the per-rid folder.</param>
    /// <param name="format">One of <see cref="ArchiveFormats.All"/>.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="BuildStageException">The directory to archive does not exist, or <paramref name="format"/> is tar.xz, which the archiver does not write yet.</exception>
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
        else
        {
            await CreateTarGz(sourceDirectory, archiveFile, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Confirms <paramref name="format"/> is one the archiver writes, so an unknown value fails
    /// naming the bad value and the allowed set, and tar.xz - which this archiver does not write
    /// yet - fails naming the format instead of falling through to the contradictory
    /// unknown-format error. The build stage already skips tar.xz per Windows-class and any-*
    /// rid, so only a Unix-based rid can reach that branch.
    /// </summary>
    /// <param name="format">One of <see cref="ArchiveFormats.All"/>.</param>
    /// <param name="archiveFile">The archive file the error message names.</param>
    /// <returns><see langword="true"/> when the format writes an archive; <see langword="false"/> for <see cref="ArchiveFormats.None"/>.</returns>
    /// <exception cref="BuildStageException"><paramref name="format"/> is tar.xz.</exception>
    /// <exception cref="ManifestException"><paramref name="format"/> is not a known archive format.</exception>
    private static bool EnsureWritableFormat(string format, FileInfo archiveFile)
    {
        switch (format)
        {
            case ArchiveFormats.None:
                return false;

            case ArchiveFormats.Zip:
            case ArchiveFormats.TarGz:
                return true;

            case ArchiveFormats.TarXz:
                throw new BuildStageException(
                    $"build: cannot create archive '{archiveFile.FullName}': tar.xz archiving is not available yet. Declare '{ArchiveFormats.Zip}' or '{ArchiveFormats.TarGz}' in 'archiveFormats' instead.");

            default:
                // The error style: names the bad value and the allowed set.
                throw new ManifestException(
                    $"Unknown archive format '{format}'. Known formats: {string.Join(", ", ArchiveFormats.All)}.");
        }
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

        foreach (string file in Directory.EnumerateFiles(sourceDirectory.FullName, "*", SearchOption.AllDirectories))
        {
            string entryName = Path.GetRelativePath(sourceDirectory.FullName, file).Replace('\\', '/');
            await writer.WriteEntryAsync(file, entryName, cancellationToken).ConfigureAwait(false);
        }
    }
}
