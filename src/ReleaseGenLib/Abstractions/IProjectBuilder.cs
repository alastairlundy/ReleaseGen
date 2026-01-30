namespace ReleaseGenLib.Abstractions;

public interface IProjectBuilder
{
    Task<bool> BuildDebugAsync(DirectoryInfo targetDirectory, 
        ProjectBuildConfiguration buildConfiguration, CancellationToken cancellationToken);

    Task<bool> BuildReleaseAsync(DirectoryInfo targetDirectory,
        ProjectBuildConfiguration projectConfiguration, CancellationToken cancellationToken);
}