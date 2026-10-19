namespace ReleaseGenCli.Commands;

using System.CommandLine;

/// <summary>
/// The chained run: executes the stages named by the manifest in manifest order, aborting at the
/// first failed stage and reporting skipped stages (D010). Stage behavior is wired by the
/// chained-run branch; this is the port's placeholder runner.
/// </summary>
public sealed class RunCommand : Command
{
    public RunCommand(IServiceProvider services) : base("run",
        "Runs the manifest's stages in manifest order (chained run).")
    {
        SetAction((parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);
            CliOutput.Note("The chained run is not wired yet; it lands with the chained-run branch.");
            return Task.FromResult(0);
        });
    }
}
