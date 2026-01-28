namespace ReleaseGenLib.Abstractions;

public interface IProjectInfoProvider
{
    ProjectInfo DetectProjectInfo(FileInfo projectFile);
    
    bool TryDetectProjectInfo(FileInfo projectFile, out ProjectInfo? projectInfo);
}