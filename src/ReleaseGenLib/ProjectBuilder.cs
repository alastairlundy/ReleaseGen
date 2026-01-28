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
    /// <param name="noRestore"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="targetFramework"></param>
    /// <param name="outputDirectory"></param>
    /// <returns></returns>
    public async Task<bool> BuildDebugAsync(DirectoryInfo targetDirectory, bool noRestore,
        CancellationToken cancellationToken, string? targetFramework = null, DirectoryInfo? outputDirectory = null)
        => await BuildConfigurationAsync("debug", targetDirectory,noRestore, cancellationToken, targetFramework);
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="targetDirectory"></param>
    /// <param name="noRestore"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="targetFramework"></param>
    /// <param name="outputDirectory"></param>
    /// <returns></returns>
    public async Task<bool> BuildReleaseAsync(DirectoryInfo targetDirectory,
        bool noRestore, CancellationToken cancellationToken, string? targetFramework = null,
        DirectoryInfo? outputDirectory = null)
        => await BuildConfigurationAsync("release", targetDirectory,noRestore, cancellationToken, targetFramework);

    private async Task<bool> BuildConfigurationAsync(string configuration, DirectoryInfo targetDirectory,
        bool noRestore, CancellationToken cancellationToken,
        string? targetFramework = null, DirectoryInfo? outputDirectory = null)
    {
        IArgumentsBuilder argumentsBuilder = new ArgumentsBuilder()
            .Add("build -c");

        argumentsBuilder = argumentsBuilder
            .Add(configuration == "release" ? "Release" : "Debug");
        
        if (targetFramework is not null)
        {
            argumentsBuilder = argumentsBuilder.Add($"-f {targetFramework}");
        }
        if (outputDirectory is not null)
        {
            argumentsBuilder = argumentsBuilder.Add($"-o {outputDirectory.FullName}");
        }
        
        if (noRestore)
        {
            argumentsBuilder = argumentsBuilder.Add("--no-restore");
        }
        
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