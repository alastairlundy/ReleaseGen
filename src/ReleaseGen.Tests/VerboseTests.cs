using Microsoft.Extensions.DependencyInjection;
using ReleaseGen.Cli.Commands;
using ReleaseGen.Cli.Helpers;

namespace ReleaseGen.Tests;

using System.CommandLine;
using ReleaseGenLib.Abstractions;

/// <summary>
/// The <c>--verbose</c> flag, the quiet/verbose conflict, and the default-error hint: a real
/// run under the flag surfaces each spawned command line, its raw output, and the record's
/// cause line; every default failure names the escape hatch on one hint line; and passing both
/// output flags fails with the conflict line instead of silently choosing a mode. The
/// planned-record mechanics the flag consumes live with <see cref="PlannedCommandTests"/>.
/// </summary>
[ReleaseGenDi]
public class VerboseTests
{
    private readonly RecordingProcessInvoker _invoker;
    private readonly IManifestLoader _manifestLoader;
    private readonly IBuildStageRunner _buildStageRunner;

    /// <summary>
    /// The <c>--verbose</c> flag, the quiet/verbose conflict, and the default-error hint on
    /// every stage command and the chained run.
    /// </summary>
    public VerboseTests(ServiceProvider services, RecordingProcessInvoker invoker)
    {
        _invoker = invoker;
        _manifestLoader = services.GetRequiredService<IManifestLoader>();
        _buildStageRunner = services.GetRequiredService<IBuildStageRunner>();
    }

    [Test]
    public async Task Passing_quiet_and_verbose_exits_1_with_the_conflict_line()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new TestCommand(_manifestLoader, _invoker), "test", "--quiet", "--verbose");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            // One error line names the conflict and carries the diagnostics tip with it, so
            // no output mode is silently chosen; the check runs before any stage work, so no
            // command is spawned and no manifest is read.
            await Assert.That(console).Contains("--quiet and --verbose conflict");
            await Assert.That(console).Contains("run with --verbose for full detail");
            await Assert.That(CountOccurrences(console, "error:")).IsEqualTo(1);
            await Assert.That(console.Contains("hint:")).IsFalse();
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Every_stage_command_and_the_chained_run_refuse_quiet_with_verbose()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            // The flag registers on each stage command and on run - the conflict check can
            // only fire where the command accepted --verbose in the first place.
            Command[] commands =
            [
                new BuildCommand(_manifestLoader, _buildStageRunner),
                new TestCommand(_manifestLoader, _invoker),
                new PackageCommand(_manifestLoader, _invoker),
                new PublishCommand(_manifestLoader, _invoker),
                new RunCommand(_manifestLoader, _invoker, _buildStageRunner),
            ];
            string[] names = ["build", "test", "package", "publish", "run"];

            foreach ((Command command, string name) in commands.Zip(names))
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        command, name, "--quiet", "--verbose");

                    await Assert.That(exitCode).IsEqualTo(1);
                });

                await Assert.That(console).Contains("--quiet and --verbose conflict");
                await Assert.That(console).Contains("run with --verbose for full detail");
                await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task A_default_error_carries_the_hint_and_a_verbose_run_does_not()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        // The renamed-away 'archive' key fails the strict parse: a manifest error is the
        // one failure a default run reports with the hint line and a verbose run reports
        // without it - the user is already in full detail.
        workspace.WriteManifest(
            """{ "targets": [], "build": { "runtimeIdentifiers": [] }, "publish": { "sources": [] }, "archive": "zip" }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string defaultConsole = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner), "build");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            string verboseConsole = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner), "build", "--verbose");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            // The terse error stays the default; the hint names the escape hatch once.
            await Assert.That(defaultConsole).Contains("error:");
            await Assert.That(defaultConsole).Contains("run with --verbose for full detail");
            await Assert.That(verboseConsole).Contains("error:");
            await Assert.That(verboseConsole.Contains("run with --verbose for full detail")).IsFalse();
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_failing_test_run_under_verbose_surfaces_the_command_line_raw_output_and_cause()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "test": { "projects": ["src/Lib.Tests/Lib.Tests.csproj"] }, "publish": { "sources": [] } }""");

        // The failure shape verbose exists for: dotnet test exits nonzero, and its own terse
        // line is all a default run would add to ReleaseGen's one-line error.
        _invoker.ExitCodeFactory = _ => 1;
        _invoker.OutputFactory = _ =>
            ("Specify a project or solution file.",
             "MSBUILD : error MSB1003: Specify a project or solution file.");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new TestCommand(_manifestLoader, _invoker), "test", "--verbose");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            // The shared record's command line, the raw child output, and ReleaseGen's
            // diagnosis all reach the console...
            await Assert.That(console).Contains("test: dotnet test");
            await Assert.That(console).Contains("Specify a project or solution file.");
            await Assert.That(console).Contains("cause:");
            await Assert.That(console).Contains("the test section names");

            // ... and no hint tells a run that already asked for full detail to ask again.
            await Assert.That(console.Contains("run with --verbose for full detail")).IsFalse();
        });
    }

    [Test]
    public async Task A_failing_test_run_without_verbose_prints_the_hint_and_no_cause()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "test": { "projects": ["src/Lib.Tests/Lib.Tests.csproj"] }, "publish": { "sources": [] } }""");

        _invoker.ExitCodeFactory = _ => 1;
        _invoker.OutputFactory = _ =>
            ("Specify a project or solution file.",
             "MSBUILD : error MSB1003: Specify a project or solution file.");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new TestCommand(_manifestLoader, _invoker), "test");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            // The default failure stays terse: the one-line error plus the hint naming the
            // escape hatch, and no diagnosis the user did not ask for.
            await Assert.That(console).Contains("test: dotnet test failed for project");
            await Assert.That(console).Contains("run with --verbose for full detail");
            await Assert.That(console.Contains("cause:")).IsFalse();
        });
    }

    [Test]
    public async Task The_chained_run_forwards_verbose_to_every_named_stage()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["executables"] } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "test": { "projects": ["src/Lib.Tests/Lib.Tests.csproj"] }, "publish": { "sources": [] } }""");
        _invoker.OnInvocation = RecordingProcessInvoker.MaterializePublishOutput;

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string plainConsole = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new RunCommand(_manifestLoader, _invoker, _buildStageRunner), "run");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                string verboseConsole = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new RunCommand(_manifestLoader, _invoker, _buildStageRunner), "run", "--verbose");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // The chain forwards the flag the way a user would pass it, so each named
                // stage runs verbose and prints its cause lines; without the flag none do.
                await Assert.That(plainConsole).Contains("run: 2 stage(s) completed successfully");
                await Assert.That(plainConsole.Contains("cause:")).IsFalse();
                await Assert.That(verboseConsole).Contains("run: 2 stage(s) completed successfully");
                await Assert.That(verboseConsole).Contains("cause:");
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="value"/>, so a test can assert a
    /// message printed exactly once rather than at least once.
    /// </summary>
    /// <param name="text">The captured console output.</param>
    /// <param name="value">The substring to count.</param>
    /// <returns>How many times <paramref name="value"/> occurs in <paramref name="text"/>.</returns>
    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
