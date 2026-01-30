using CliInvoke.Builders;

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
        IArgumentsBuilder argumentsBuilder = new ArgumentsBuilder()
            .Add("build -c");

        argumentsBuilder = argumentsBuilder
            .Add(configuration == "release" ? "Release" : "Debug");
        
        if (buildConfiguration.TargetFramework is not null)
        {
            argumentsBuilder = argumentsBuilder.Add($"-f {buildConfiguration.TargetFramework}");
        }

        if (buildConfiguration.RuntimeIdentifier is not null)
        {
            argumentsBuilder = argumentsBuilder.Add($"-r {buildConfiguration.RuntimeIdentifier}");
        }
        
        if (buildConfiguration.OutputDirectory is not null)
        {
            argumentsBuilder = argumentsBuilder.Add($"-o {buildConfiguration.OutputDirectory.FullName}");
        }
        
        if (buildConfiguration.PublishSingleFile)
        {
            argumentsBuilder = argumentsBuilder.Add("--publish-single-file");
        }

        if (buildConfiguration.SelfContained) 
            argumentsBuilder = argumentsBuilder.Add("--self-contained");
        
        if(buildConfiguration.PublishAoT)
            argumentsBuilder = argumentsBuilder.Add("--publish-aot");
        
        if(buildConfiguration is { PublishTrimmed: true, PublishAoT: false })
            argumentsBuilder = argumentsBuilder.Add("--publish-trimmed");
        
        if (buildConfiguration.NoRestore)
            argumentsBuilder = argumentsBuilder.Add("--no-restore");
        
        if(!buildConfiguration.UseAppHost)
            argumentsBuilder = argumentsBuilder.Add("--use-apphost false");
        
        if(!buildConfiguration.PublishReadyToRun)
            argumentsBuilder = argumentsBuilder.Add("--ready-to-run");
        
        string arguments = argumentsBuilder.ToString();

        IProcessConfigurationBuilder processConfigurationBuilder = new ProcessConfigurationBuilder(_dotnetSdkExecutable)
            .SetArguments(arguments)
            .SetWorkingDirectory(targetDirectory.FullName)
            .ConfigureWindowCreation(false)
            .RedirectStandardOutput(true)
            .RedirectStandardError(true);
        
        using ProcessConfiguration processConfiguration =  processConfigurationBuilder.Build();

        ProcessResult result = await _processInvoker.ExecuteAsync(processConfiguration, 
            ProcessExitConfiguration.DefaultNoException, false, cancellationToken);
        
        return result.ExitCode == 0;
    }
}