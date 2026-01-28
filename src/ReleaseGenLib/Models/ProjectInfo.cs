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
    
    public bool PublishAoT { get; set; }
    
    public bool PublishTrimmed  { get; set; }
    
    public bool PublishReadyToRun { get; set; }
    
    public bool PublishSingleFile { get; set; }
    
    public bool SelfContained { get; set; }
    
    public bool UseAppHost { get; set; }
}