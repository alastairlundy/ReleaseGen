namespace ReleaseGenLib.Models;

public class ProjectBuildConfiguration
{
    public string? TargetFramework { get; set; }
    
    public string? RuntimeIdentifier { get; set; }
    
    public bool PublishAoT { get; set; }
    
    public bool PublishTrimmed  { get; set; }
    
    public bool PublishReadyToRun { get; set; }
    
    public bool PublishSingleFile { get; set; }
    
    public bool SelfContained { get; set; }
    
    public bool UseAppHost { get; set; }
    
    public bool NoRestore { get; set; }
    
    public DirectoryInfo? OutputDirectory { get; set; }
}