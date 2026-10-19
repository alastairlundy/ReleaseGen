namespace ReleaseGenLib.Models;

public class ProjectInfo
{
    public ProjectInfo(string[] targetFrameworks, string[] targetRuntimeIdentifiers)
    {
        ArgumentNullException.ThrowIfNull(targetFrameworks);
        ArgumentNullException.ThrowIfNull(targetRuntimeIdentifiers);
        
        TargetFrameworks = targetFrameworks;
        
        TargetRuntimeIdentifiers = targetRuntimeIdentifiers;
    }
    
    public string[] TargetFrameworks { get; set; }

    public string[] TargetRuntimeIdentifiers { get; set; }
    
    public ProjectBuildConfiguration BuildConfiguration { get; set; } = new();
}