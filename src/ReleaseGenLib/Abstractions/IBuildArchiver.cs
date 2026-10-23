using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib.Abstractions;

public interface IBuildArchiver
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
    Task CreateArchiveAsync(DirectoryInfo sourceDirectory, FileInfo archiveFile, string format, CancellationToken cancellationToken);
}