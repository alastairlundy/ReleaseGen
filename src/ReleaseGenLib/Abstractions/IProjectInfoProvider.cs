namespace ReleaseGenLib.Abstractions;

public interface IProjectInfoProvider
{
    ProjectInfo DetectProjectInfo(FileInfo projectFile);

    bool TryDetectProjectInfo(FileInfo projectFile, out ProjectInfo? projectInfo);

    /// <summary>
    /// Evaluates the project file with Microsoft.Build and returns its OutputType, defaulting to
    /// 'Library' when the project does not set it. Used by the D020 manifest cross-check.
    /// </summary>
    /// <param name="projectFile">The project file to evaluate.</param>
    /// <returns>The evaluated OutputType value.</returns>
    string GetOutputType(FileInfo projectFile);
}