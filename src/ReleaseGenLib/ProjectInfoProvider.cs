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
    /// The environment variables in-process MSBuild export into the host process when a toolset
    /// initializes (observed: MSBuildSDKsPath, MSBUILD_EXE_PATH, MSBuildExtensionsPath*,
    /// MSBuildToolsPath). Left in place they leak into child processes this library spawns - a
    /// <c>dotnet publish</c> would resolve the SDK of the MSBuildLocator-registered toolset
    /// instead of its own muxer selection, so they are saved and restored around every
    /// evaluation.
    /// </summary>
    private static readonly string[] MsBuildEnvironmentVariables =
    [
        "MSBuildSDKsPath",
        "MSBuildExtensionsPath",
        "MSBuildExtensionsPath32",
        "MSBuildExtensionsPath64",
        "MSBuildExtensionsPathOverride",
        "MSBuildToolsPath",
        "MSBuildLoadMicrosoftTargetsReadOnly",
        "MSBUILD_EXE_PATH",
        "DOTNET_HOST_PATH",
    ];

    /// <summary>
    /// Registers the MSBuild SDK resolver once, before any Microsoft.Build assembly loads, so
    /// that SDK-style projects can be evaluated in-process (the evaluation path).
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

    /// <summary>
    /// Runs an MSBuild evaluation with the MSBuild-owned environment variables scoped to the
    /// call: the previous values are restored when the evaluation completes, so processes
    /// spawned afterwards (the build stage's per-rid publishes) resolve their own SDK
    /// rather than inheriting the toolset's paths (an evaluation-path side effect).
    /// </summary>
    /// <typeparam name="T">The evaluation result type.</typeparam>
    /// <param name="evaluation">The evaluation to run.</param>
    /// <returns>The evaluation result.</returns>
    private static T RunWithScopedMsBuildEnvironment<T>(Func<T> evaluation)
    {
        Dictionary<string, string?> savedVariables = new(MsBuildEnvironmentVariables.Length);

        foreach (string variable in MsBuildEnvironmentVariables)
        {
            savedVariables[variable] = Environment.GetEnvironmentVariable(variable);
        }

        try
        {
            return evaluation();
        }
        finally
        {
            foreach (KeyValuePair<string, string?> variable in savedVariables)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }
        }
    }

    public ProjectInfo DetectProjectInfo(FileInfo projectFile) =>
        RunWithScopedMsBuildEnvironment(() =>
        {
            EnsureSdkResolverRegistered();
            return DetectProjectInfoCore(projectFile);
        });

    /// <summary>
    /// Evaluates the project file with Microsoft.Build and returns its OutputType.
    /// </summary>
    /// <param name="projectFile">The project file to evaluate.</param>
    /// <returns>The evaluated OutputType value, or 'Library' when the project does not set one.</returns>
    public string GetOutputType(FileInfo projectFile) =>
        RunWithScopedMsBuildEnvironment(() =>
        {
            EnsureSdkResolverRegistered();
            return GetOutputTypeCore(projectFile);
        });

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

        try
        {
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
        finally
        {
            // Unload so a later load of the same project path in this process re-evaluates
            // instead of failing on a duplicate entry in the global collection: the chained
            // run loads the manifest once per stage, and several targets may name one project.
            project.ProjectCollection.UnloadProject(project);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GetOutputTypeCore(FileInfo projectFile)
    {
        Project project = Project.FromFile(projectFile.FullName, new ProjectOptions()
        {
            Interactive = false
        });

        try
        {
            string outputType = project.GetPropertyValue("OutputType");

            // MSBuild defaults an unset OutputType to Library.
            return outputType == string.Empty ? "Library" : outputType;
        }
        finally
        {
            // Same unload as DetectProjectInfoCore: repeated in-process manifest loads (the
            // chained run re-loads per stage) must not collide on the global collection.
            project.ProjectCollection.UnloadProject(project);
        }
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
