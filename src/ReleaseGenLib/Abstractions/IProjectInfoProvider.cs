namespace ReleaseGenLib.Abstractions;

public interface IProjectInfoProvider
{
    ProjectInfo DetectProjectInfo(FileInfo projectFile);

    bool TryDetectProjectInfo(FileInfo projectFile, out ProjectInfo? projectInfo);

    /// <summary>
    /// Evaluates the project file with Microsoft.Build once and returns the expanded
    /// properties ReleaseGen reads: OutputType (defaulting to 'Library'), PackAsTool, and
    /// IsPackable. Used by the manifest's kind cross-check and by init's repo detection, so
    /// both see the same pass and the same props-driven expansion - raw project text is
    /// never read.
    /// </summary>
    /// <param name="projectFile">The project file to evaluate.</param>
    /// <returns>The expanded properties of the evaluated project.</returns>
    ProjectEvaluation EvaluateProject(FileInfo projectFile);
}