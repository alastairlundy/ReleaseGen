using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib;

using System.Formats.Tar;
using System.IO.Compression;
using ReleaseGenLib.Models;

/// <summary>
/// Creates the per-(target, rid) release archives: zip by default via
/// System.IO.Compression.ZipFile, tar.gz via System.Formats.Tar plus GZipStream, and none to
/// leave the per-rid folder unarchived. Archives are written beside the per-rid folder under the
/// local output root and are never uploaded - the only network destination in v1 is the publish
/// stage's NuGet source. Both APIs are in-box for net10.0, so no package is added.
/// </summary>
public static class BuildArchiver
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
    /// <exception cref="BuildStageException">The directory to archive does not exist.</exception>
    /// <exception cref="ManifestException"><paramref name="format"/> is not a known archive format.</exception>
    public static async Task CreateArchive(DirectoryInfo sourceDirectory, FileInfo archiveFile, string format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(archiveFile);

        switch (format)
        {
            case ArchiveFormats.None:
                return;

            case ArchiveFormats.Zip:
                break;

            case ArchiveFormats.TarGz:
                break;

            default:
                // The error style: names the bad value and the allowed set.
                throw new ManifestException(
                    $"Unknown archive format '{format}'. Known formats: {string.Join(", ", ArchiveFormats.All)}.");
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
    /// Writes the source folder's file tree as a gzip-compressed tar archive, with entry names
    /// relative to the source folder.
    /// </summary>
    /// <param name="sourceDirectory">The per-rid output folder to archive.</param>
    /// <param name="archiveFile">The .tar.gz file to create.</param>
    /// <param name="cancellationToken"></param>
    private static async Task CreateTarGz(DirectoryInfo sourceDirectory, FileInfo archiveFile, CancellationToken cancellationToken)
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
