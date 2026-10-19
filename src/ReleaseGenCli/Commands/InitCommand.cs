namespace ReleaseGenCli.Commands;

using System.CommandLine;

/// <summary>
/// The init stage: writes a minimal valid <c>releasegen.json</c> manifest (T008).
/// Stage behavior is wired by the init-stage branch; this is the port's placeholder runner.
/// </summary>
public sealed class InitCommand : Command
{
    public InitCommand(IServiceProvider services) : base("init", "Writes a minimal valid releasegen.json manifest.")
    {
        SetAction((parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);
            CliOutput.Note("The init stage is not wired yet; it lands with the init-stage branch.");
            return Task.FromResult(0);
        });
    }
}
