namespace ReleaseGenCli.Commands;

using System.CommandLine;

/// <summary>
/// The build stage: per-rid <c>dotnet publish -c Release</c> driven by the manifest's build
/// section (T009, T010). Stage behavior is wired by the build-stage branch; this is the port's
/// placeholder runner.
/// </summary>
public sealed class BuildCommand : Command
{
    public BuildCommand(IServiceProvider services) : base("build", "Builds release binaries for the manifest's targets.")
    {
        SetAction((parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);
            CliOutput.Note("The build stage is not wired yet; it lands with the build-stage branch.");
            return Task.FromResult(0);
        });
    }
}
