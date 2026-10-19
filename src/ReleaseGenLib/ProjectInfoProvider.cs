using System.Runtime.CompilerServices;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;

namespace ReleaseGenLib;

public class ProjectInfoProvider : IProjectInfoProvider
{
    public ProjectInfoProvider()
    {

    }

    private static bool _sdkResolverRegistered;

    /// <summary>
    /// Registers the MSBuild SDK resolver once, before any Microsoft.Build assembly loads, so
    /// that SDK-style projects can be evaluated in-process (D020 evaluation path).
    /// This method must not reference Microsoft.Build types: the calling methods JIT-bind those
    /// assemblies only after registration completes.
    /// </summary>
    private static void EnsureSdkResolverRegistered()
    {
        if (_sdkResolverRegistered)
        {
            return;
        }

        MSBuildLocator.RegisterDefaults();
        _sdkResolverRegistered = true;
    }

    public ProjectInfo DetectProjectInfo(FileInfo projectFile)
    {
        EnsureSdkResolverRegistered();
        return DetectProjectInfoCore(projectFile);
    }

    /// <summary>
    /// Evaluates the project file with Microsoft.Build and returns its OutputType (D020).
    /// </summary>
    /// <param name="projectFile">The project file to evaluate.</param>
    /// <returns>The evaluated OutputType value, or 'Library' when the project does not set one.</returns>
    public string GetOutputType(FileInfo projectFile)
    {
        EnsureSdkResolverRegistered();
        return GetOutputTypeCore(projectFile);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProjectInfo DetectProjectInfoCore(FileInfo projectFile)
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

            targets = targetFrameworkString.Split(';');
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GetOutputTypeCore(FileInfo projectFile)
    {
        Project project = Project.FromFile(projectFile.FullName, new ProjectOptions()
        {
            Interactive = false
        });

        string outputType = project.GetPropertyValue("OutputType");

        // MSBuild defaults an unset OutputType to Library.
        return outputType == string.Empty ? "Library" : outputType;
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
