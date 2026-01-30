using DotExtensions.IO.Files;
using ReleaseGenLib.Models;
using Spectre.Console;

namespace ReleaseGenCli.Commands;

[CliCommand(Name = "build", Description = "Builds a project",
    Parent = typeof(RootCommand))]
public class BuildCommand
{
    private readonly IProjectBuilder _projectBuilder;
    private readonly IProjectInfoProvider _projectInfoProvider;

    public BuildCommand(IProjectBuilder projectBuilder, IProjectInfoProvider projectInfoProvider)
    {
        _projectBuilder = projectBuilder;
        _projectInfoProvider = projectInfoProvider;
    }
    
    [CliOption(Name = "--interactive", Alias = "-i", Required = false)]
    [DefaultValue(false)]
    public bool Interactive { get; set; }
    
    [CliArgument(Order = 0, Name = "<Path to Project file>", Required = true)]
    public string ProjectPath { get; set; }
    
    [CliOption(Name = "--ready-to-run", Required = false)]
    [DefaultValue(false)]
    public bool ReadyToRun { get; set; }
    
    [CliOption(Name = "--publish-aot", Required = false)]
    [DefaultValue(false)]
    public bool PublishAot { get; set; }
    
    [CliOption(Name = "--publish-trimmed", Required = false)]
    public bool PublishTrimmed { get; set; }
    
    [CliOption(Name = "--self-contained", Required = false)]
    public bool SelfContained { get; set; }
    
    [CliOption(Name = "--use-app-host")]
    [DefaultValue(true)]
    public bool UseAppHost { get; set; }
    
    [CliOption(Name = "--configuration", Alias = "-c", Required = false)]
    [DefaultValue("release")]
    public string Configuration { get; set; }
    
    [CliOption(Name = "--framework", Alias = "-f", Required = false)]
    [DefaultValue("")]
    public string TargetFramework { get; set; }
    
    [CliOption(Name = "--runtime-identifier", Alias = "-r", Required = false)]
    [DefaultValue("")]
    public string RuntimeIdentifier { get; set; }
    
    
    [CliOption(Name = "--no-restore", Required = false)]
    [DefaultValue(false)]
    public bool NoRestore { get; set; }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ProjectPath);
        
        string? newProjectPath = ProjectPath;
        
        if (Path.GetExtension(ProjectPath) != string.Empty)
        {
            newProjectPath = Path.GetDirectoryName(ProjectPath) ??
                             new FileInfo(newProjectPath).Directory?.FullName;
        }

        newProjectPath ??= ProjectPath;
        
        FileInfo?  projectFile = new DirectoryInfo(newProjectPath)
            .EnumerateFiles("*", new EnumerationOptions()
            {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            })
            .FirstOrDefault(f => f.Name.StartsWith(ProjectPath) && 
                                 (f.Name.EndsWith(".csproj") || f.Name.EndsWith(".vbproj") || f.Name.EndsWith(".fsproj")));

        if (projectFile is null)
        {
            AnsiConsole.WriteException(new FileNotFoundException($"Could not find .NET project file in path: {ProjectPath}"));
            return -1;
        }

        ProjectInfo projectInfo =
            _projectInfoProvider.DetectProjectInfo(projectFile);
    
        ProjectBuildConfiguration buildConfiguration = projectInfo.BuildConfiguration;
        
        if(PublishAot != buildConfiguration.PublishAoT)
            buildConfiguration.PublishAoT = PublishAot;
        
        if(PublishTrimmed != buildConfiguration.PublishTrimmed)
            buildConfiguration.PublishTrimmed = PublishTrimmed;
        
        if(SelfContained != buildConfiguration.SelfContained)
            buildConfiguration.SelfContained = SelfContained;
        
        if(ReadyToRun != buildConfiguration.PublishReadyToRun)
            buildConfiguration.PublishReadyToRun = ReadyToRun;
        
        if(SelfContained != buildConfiguration.SelfContained)
            buildConfiguration.SelfContained = SelfContained;
        
        if(UseAppHost != buildConfiguration.UseAppHost)
            buildConfiguration.UseAppHost = UseAppHost;
        
        if(NoRestore != buildConfiguration.NoRestore)
            buildConfiguration.NoRestore = NoRestore;
        
        string configuration = "release";
        
        if(Configuration.ToLower().Equals("debug"))
            configuration = "debug";

        bool success;

        if (configuration.ToLower().Equals("release"))
        {
            success = await _projectBuilder.BuildReleaseAsync(projectFile.GetDirectory(), buildConfiguration,
                cancellationToken);
        }
        else
        {
            success =  await _projectBuilder.BuildDebugAsync(projectFile.GetDirectory(),
                buildConfiguration,
                CancellationToken.None);
        }
        
        return success ? 0 : -1;
    }
}