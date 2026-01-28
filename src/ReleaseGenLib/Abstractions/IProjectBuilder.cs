namespace ReleaseGenLib.Abstractions;

public interface IProjectBuilder
{
    Task<bool> BuildDebugAsync(DirectoryInfo targetDirectory,
        bool noRestore, CancellationToken cancellationToken, string? targetFramework, DirectoryInfo? outputDirectory);

    Task<bool> BuildReleaseAsync(DirectoryInfo targetDirectory,
        bool noRestore, CancellationToken cancellationToken, string? targetFramework, DirectoryInfo? outputDirectory);
}