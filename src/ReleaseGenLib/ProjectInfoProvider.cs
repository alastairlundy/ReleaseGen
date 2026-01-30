using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;

namespace ReleaseGenLib;

public class ProjectInfoProvider : IProjectInfoProvider
{
    public ProjectInfoProvider()
    {
        
    }
    
    public ProjectInfo DetectProjectInfo(FileInfo projectFile)
    {
        if (!projectFile.Name.EndsWith(".csproj") && !projectFile.Name.EndsWith(".vbproj") &&
            !projectFile.Name.EndsWith(".fsproj"))
        {
            throw new ArgumentException(
                $"Project file must be a .NET project file with a file extension of .csproj, .fsproj, or .vbproj . Found {Path.GetExtension(projectFile.Name)}");
        }
        
        Project project = Project.FromFile(projectFile.FullName, new ProjectOptions()
        {
            Interactive = false
        });

        string[] targets;

        bool usesPlural = project.GetPropertyValue("TargetFramework") == string.Empty;

        ProjectInfo projectInfo = new ProjectInfo([], []);
        
        if (usesPlural)
        {
            string targetFrameworkString = project.GetPropertyValue("TargetFrameworks");

            targets  =  targetFrameworkString.Split(';');
        }
        else
        {
            targets = [project.GetPropertyValue("TargetFramework")];
        }
        
        string publishAoTValue = project.GetPropertyValue("PublishAoT");
        projectInfo.BuildConfiguration.PublishAoT = publishAoTValue != string.Empty;

        if (projectInfo.BuildConfiguration.PublishAoT)
        {
            projectInfo.BuildConfiguration.PublishTrimmed = true;
            projectInfo.BuildConfiguration.PublishReadyToRun = false;
            projectInfo.BuildConfiguration.SelfContained = true;
        }
        else
        {
            projectInfo.BuildConfiguration.PublishTrimmed = project.GetPropertyValue("PublishTrimmed") != string.Empty;
            
            projectInfo.BuildConfiguration.PublishReadyToRun = project.GetPropertyValue("PublishReadyToRun") != string.Empty;
            projectInfo.BuildConfiguration.SelfContained = project.GetPropertyValue("SelfContained") != string.Empty;
        }
        
        projectInfo.BuildConfiguration.PublishSingleFile = project.GetPropertyValue("PublishSingleFile") != string.Empty;
        projectInfo.BuildConfiguration.UseAppHost = project.GetPropertyValue("UseAppHost") != string.Empty;

        string[] runtimeIdentifiers = project.GetPropertyValue("RuntimeIdentifier") != string.Empty
            ? project.GetPropertyValue("RuntimeIdentifier").Split(';')
            : [];
        
        projectInfo.TargetFrameworks = targets;
        projectInfo.TargetRuntimeIdentifiers = runtimeIdentifiers;
        
        return projectInfo;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="projectFile"></param>
    /// <param name="projectInfo"></param>
    /// <returns></returns>
    public bool TryDetectProjectInfo(FileInfo projectFile, out ProjectInfo? projectInfo)
    {
        try
        {
            ProjectInfo project = DetectProjectInfo(projectFile);
            projectInfo = project;
            return true;
        }
        catch
        {
            projectInfo = null;
            return false;
        }
    }
}