namespace ReleaseGenCli.Commands;

[CliCommand(Name = "package")]
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
}