namespace ReleaseGenLib;

using System.Formats.Tar;
using System.IO.Compression;
using ReleaseGenLib.Models;

/// <summary>
/// Creates the per-(target, rid) release archives (T016): zip by default via
/// System.IO.Compression.ZipFile, tar.gz via System.Formats.Tar plus GZipStream, and none to
/// leave the per-rid folder unarchived. Archives are written beside the per-rid folder under the
/// local output root and are never uploaded - the only network destination in v1 is the publish
/// stage's NuGet source (D012). Both APIs are in-box for net10.0, so no package is added (T005).
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
    /// <param name="format">One of <see cref="ArchiveFormats.All"/> (T016).</param>
    /// <exception cref="BuildStageException">The directory to archive does not exist.</exception>
    /// <exception cref="ManifestException"><paramref name="format"/> is not a known archive format.</exception>
    public static void CreateArchive(DirectoryInfo sourceDirectory, FileInfo archiveFile, string format)
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
                // D009 error style: names the bad value and the allowed set.
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

        if (format == ArchiveFormats.Zip)
        {
            // includeBaseDirectory: false - the archive is named <project-name>-<rid>.zip, so the
            // per-rid project folder itself does not need to wrap the entries (T016).
            ZipFile.CreateFromDirectory(
                sourceDirectory.FullName, archiveFile.FullName, CompressionLevel.Optimal, false);
        }
        else
        {
            CreateTarGz(sourceDirectory, archiveFile);
        }
    }

    /// <summary>
    /// Writes the source folder's file tree as a gzip-compressed tar archive, with entry names
    /// relative to the source folder (T016).
    /// </summary>
    /// <param name="sourceDirectory">The per-rid output folder to archive.</param>
    /// <param name="archiveFile">The .tar.gz file to create.</param>
    private static void CreateTarGz(DirectoryInfo sourceDirectory, FileInfo archiveFile)
    {
        using FileStream fileStream = new(archiveFile.FullName, FileMode.Create, FileAccess.Write, FileShare.None);
        using GZipStream gzipStream = new(fileStream, CompressionLevel.Optimal, false);
        using TarWriter writer = new(gzipStream, false);

        foreach (string file in Directory.EnumerateFiles(sourceDirectory.FullName, "*", SearchOption.AllDirectories))
        {
            string entryName = Path.GetRelativePath(sourceDirectory.FullName, file).Replace('\\', '/');
            writer.WriteEntry(file, entryName);
        }
    }
}
