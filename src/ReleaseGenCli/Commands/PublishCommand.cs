namespace ReleaseGenCli.Commands;

using System.CommandLine;

/// <summary>
/// The publish stage: <c>dotnet nuget push</c> per package with sources from the manifest or the
/// nuget.org default, and the API key from the environment or a CLI option (T012). Stage
/// behavior is wired by the publish-stage branch; this is the port's placeholder runner.
/// </summary>
public sealed class PublishCommand : Command
{
    public PublishCommand(IServiceProvider services) : base("publish",
        "Publishes each target's declared package kinds to the configured NuGet sources.")
    {
        SetAction((parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);
            CliOutput.Note("The publish stage is not wired yet; it lands with the publish-stage branch.");
            return Task.FromResult(0);
        });
    }
}
