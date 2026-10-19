namespace ReleaseGenLib;

/// <summary>
/// 
/// </summary>
public class ProjectBuilder : IProjectBuilder
{
    private readonly IProcessInvoker _processInvoker;
    private readonly string _dotnetSdkExecutable;
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="processInvoker"></param>
    public ProjectBuilder(IProcessInvoker processInvoker)
    {
        _processInvoker = processInvoker;
        _dotnetSdkExecutable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="targetDirectory"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="buildConfiguration"></param>
    /// <returns></returns>
    public async Task<bool> BuildDebugAsync(DirectoryInfo targetDirectory, ProjectBuildConfiguration buildConfiguration,
        CancellationToken cancellationToken)
        => await BuildConfigurationAsync("debug", targetDirectory, buildConfiguration, cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="targetDirectory"></param>
    /// <param name="buildConfiguration"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<bool> BuildReleaseAsync(DirectoryInfo targetDirectory, ProjectBuildConfiguration buildConfiguration,
        CancellationToken cancellationToken)
        => await BuildConfigurationAsync("release", targetDirectory, buildConfiguration, cancellationToken);

    private async Task<bool> BuildConfigurationAsync(string configuration, DirectoryInfo targetDirectory, 
        ProjectBuildConfiguration buildConfiguration, CancellationToken cancellationToken)
    {
        List<string> argumentList = ["build", "-c", configuration == "release" ? "Release" : "Debug"];

        if (buildConfiguration.TargetFramework is not null)
        {
            argumentList.Add("-f");
            argumentList.Add(buildConfiguration.TargetFramework);
        }

        if (buildConfiguration.RuntimeIdentifier is not null)
        {
            argumentList.Add("-r");
            argumentList.Add(buildConfiguration.RuntimeIdentifier);
        }
        
        if (buildConfiguration.OutputDirectory is not null)
        {
            argumentList.Add("-o");
            argumentList.Add(buildConfiguration.OutputDirectory.FullName);
        }
        
        if (buildConfiguration.PublishSingleFile)
        {
            argumentList.Add("--publish-single-file");
        }

        if (buildConfiguration.SelfContained) 
            argumentList.Add("--self-contained");
        
        if(buildConfiguration.PublishAoT)
            argumentList.Add("--publish-aot");
        
        if(buildConfiguration is { PublishTrimmed: true, PublishAoT: false })
            argumentList.Add("--publish-trimmed");
        
        if (buildConfiguration.NoRestore)
            argumentList.Add("--no-restore");
        
        if(!buildConfiguration.UseAppHost)
            argumentList.Add("--use-apphost false");
        
        if(!buildConfiguration.PublishReadyToRun)
            argumentList.Add("--ready-to-run");

        ProcessConfiguration processConfiguration = new ProcessConfiguration(_dotnetSdkExecutable, argumentList)
        {
            WorkingDirectoryPath = targetDirectory.FullName
        };

        ProcessResult result = await _processInvoker.ExecuteAsync(processConfiguration,
            ProcessExitConfiguration.Default, cancellationToken);
        
        return result.ExitCode == 0;
    }
}