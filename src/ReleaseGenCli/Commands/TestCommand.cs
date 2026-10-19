namespace ReleaseGenCli.Commands;

using System.CommandLine;

/// <summary>
/// The test stage: <c>dotnet test</c> on the manifest's named test projects, or from the current
/// directory when none are listed (T011). Stage behavior is wired by the test-stage branch; this
/// is the port's placeholder runner.
/// </summary>
public sealed class TestCommand : Command
{
    public TestCommand(IServiceProvider services) : base("test", "Runs dotnet test on the manifest's test projects.")
    {
        SetAction((parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);
            CliOutput.Note("The test stage is not wired yet; it lands with the test-stage branch.");
            return Task.FromResult(0);
        });
    }
}
