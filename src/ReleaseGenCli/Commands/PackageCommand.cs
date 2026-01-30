namespace ReleaseGenCli.Commands;

[CliCommand(Name = "package", )]
public class PackageCommand
{
    public abstract class SharedArgs
    {
        [CliOption(Name = "--use-app-host")]
        [DefaultValue(true)]
        public bool UseAppHost { get; set; }

        [CliOption(Name = "--configuration", Alias = "-c", Required = false)]
        [DefaultValue("release")]
        public string Configuration { get; set; } = "";

        [CliOption(Name = "--framework", Alias = "-f", Required = false)]
        public string? TargetFramework { get; set; }
        
        [CliArgument(Order = 0, Name = "<Path to Project file>", Required = true)]
        public string? ProjectPath { get; set; }
    }

    
    [CliCommand(Name = "tool", Description = "Packages a .NET project as a .NET tool.")]
    public class PackageToolCommand : SharedArgs
    {

        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            
        }
    }
    
    
    [CliCommand(Name = "executable", Alias = "exe", Description = "Packages a .NET executable project for release.",
        Parent = typeof(RootCommand))]
    public class PackageExecutableCommand : SharedArgs
    {
        private readonly IProjectPackager _projectPackager;

        public PackageExecutableCommand(IProjectPackager projectPackager)
        {
            _projectPackager = projectPackager;
        }

        [CliOption(Name = "--interactive", Alias = "-i", Required = false)]
        [DefaultValue(false)]
        public bool Interactive { get; set; }

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


        [CliOption(Name = "--runtime-identifier", Alias = "-r", Required = false)]
        public string[] RuntimeIdentifier { get; set; }

        public Task<int> RunAsync(CancellationToken cancellationToken)
        {
            
            
            
        }
    }
}