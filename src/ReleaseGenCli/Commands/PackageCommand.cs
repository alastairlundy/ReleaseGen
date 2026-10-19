namespace ReleaseGenCli.Commands;

using System.CommandLine;

/// <summary>
/// The package stage: produces only the artifact kinds each manifest target declares (D013).
/// Stage behavior is wired by the package-stage branch; this is the port's placeholder runner.
/// </summary>
public sealed class PackageCommand : Command
{
    public PackageCommand(IServiceProvider services) : base("package",
        "Packages each target's declared artifact kinds.")
    {
        SetAction((parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);
            CliOutput.Note("The package stage is not wired yet; it lands with the package-stage branch.");
            return Task.FromResult(0);
        });
    }
}
