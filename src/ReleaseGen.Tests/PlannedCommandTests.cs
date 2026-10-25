using Microsoft.Extensions.DependencyInjection;
using ReleaseGen.Cli.Commands;

namespace ReleaseGen.Tests;

using CliInvoke.Core;
using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The shared planned-command record and the runner's execute-versus-print decision: one plan
/// serves both modes so the printed lines cannot diverge, the dry-run report never runs the
/// record's work or its runtime guards, and diagnosis text is suppressed until an execution has
/// verified something. The runner-level tests need no injected stage service beyond the
/// recording invoker; the build-stage tests drive the real build through the test's own DI
/// container, serialized by the TestWorkspace command-level lock.
/// </summary>
[ReleaseGenDi]
public class PlannedCommandTests
{
    private readonly RecordingProcessInvoker _invoker;
    private readonly IManifestLoader _manifestLoader;
    private readonly IBuildStageRunner _buildStageRunner;

    /// <summary>
    /// The shared planned-command record and the runner's execute-versus-print decision: one plan
    /// serves both modes so the printed lines cannot diverge, the dry-run report never runs the
    /// record's work or its runtime guards, and diagnosis text is suppressed until an execution
    /// has verified something.
    /// </summary>
    public PlannedCommandTests(ServiceProvider services, RecordingProcessInvoker invoker)
    {
        _invoker = invoker;
        _manifestLoader = services.GetRequiredService<IManifestLoader>();
        _buildStageRunner = services.GetRequiredService<IBuildStageRunner>();
    }

    [Test]
    public async Task The_dry_run_prints_a_planned_command_without_executing_it()
    {
        PlannedCommandRunner runner = new(_invoker);
        PlannedCommand command = ProcessCommand();
        List<string> printed = [];

        BufferedProcessResult? result = await runner.RunAsync(
            command, StageRunMode.DryRun, printed.Add, CancellationToken.None);

        // The plan printed; nothing ran, so there is no process result to interpret.
        await Assert.That(result).IsNull();
        await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        await Assert.That(printed).Contains(command.CommandLine);
    }

    [Test]
    public async Task The_dry_run_print_and_the_real_run_share_one_plan()
    {
        PlannedCommandRunner runner = new(_invoker);
        PlannedCommand command = ProcessCommand();
        List<string> dryRunPrint = [];
        List<string> realRunPrint = [];

        BufferedProcessResult? dryRunResult = await runner.RunAsync(
            command, StageRunMode.DryRun, dryRunPrint.Add, CancellationToken.None);
        BufferedProcessResult? realRunResult = await runner.RunAsync(
            command, StageRunMode.Execute, realRunPrint.Add, CancellationToken.None);

        // One record, one command line, both modes: the plan cannot diverge across flags.
        await Assert.That(dryRunResult).IsNull();
        await Assert.That(realRunResult).IsNotNull();
        await Assert.That(dryRunPrint).IsEquivalentTo(realRunPrint);
        await Assert.That(dryRunPrint[0]).IsEqualTo(command.CommandLine);
        await Assert.That(_invoker.Invocations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_guarded_side_effect_prints_as_conditional_without_running_its_guard()
    {
        PlannedCommandRunner runner = new(_invoker);
        bool guardRan = false;
        PlannedCommand archive = new()
        {
            CommandLine = "build: archiving 'release/win-x64/Alpha' to 'release/Alpha-win-x64.zip' (zip).",
            ExecuteAsync = _ =>
            {
                // Stands in for the archiver's source-folder check: only a real run may reach it.
                guardRan = true;
                return Task.CompletedTask;
            },
            ArtifactPaths = ["release/Alpha-win-x64.zip"],
            Condition = "output folder 'release/win-x64/Alpha' exists",
        };
        List<string> printed = [];

        BufferedProcessResult? result = await runner.RunAsync(
            archive, StageRunMode.DryRun, printed.Add, CancellationToken.None);

        // The report describes the guard as conditional instead of running it, and the work
        // itself never starts.
        await Assert.That(result).IsNull();
        await Assert.That(guardRan).IsFalse();
        await Assert.That(printed).Contains(
            $"{archive.CommandLine} (conditional: {archive.Condition})");

        // A real run executes the work - the guard lives inside it, not in the runner.
        await runner.RunAsync(archive, StageRunMode.Execute, null, CancellationToken.None);
        await Assert.That(guardRan).IsTrue();
    }

    [Test]
    public async Task Diagnosis_is_suppressed_when_unverified_and_reported_after_execution()
    {
        PlannedCommandRunner runner = new(_invoker);
        int diagnosisCalls = 0;
        List<string> diagnoses = [];
        PlannedCommand command = ProcessCommand(() =>
        {
            diagnosisCalls++;
            return "exit code 1 usually means the project path does not exist.";
        });

        await runner.RunAsync(
            command, StageRunMode.DryRun, null, CancellationToken.None, diagnoses.Add);

        // Unverified: the dry run never invokes the callback, so no diagnosis text can appear.
        await Assert.That(diagnosisCalls).IsEqualTo(0);
        await Assert.That(diagnoses.Count).IsEqualTo(0);

        await runner.RunAsync(
            command, StageRunMode.Execute, null, CancellationToken.None, diagnoses.Add);

        await Assert.That(diagnosisCalls).IsEqualTo(1);
        await Assert.That(diagnoses).Contains("exit code 1 usually means the project path does not exist.");
    }

    [Test]
    public async Task A_dry_run_build_prints_the_plan_without_publishing_or_writing()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Alpha/Alpha.csproj", "kinds": ["executables"] } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "publish": { "sources": [] } }""");
        _invoker.OnInvocation = RecordingProcessInvoker.MaterializePublishOutput;

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner, StageRunMode.DryRun), "build");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // Nothing ran and nothing was written - including the stale-output cleanup, so a
            // pre-existing archive would survive untouched.
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
            await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();

            // The plan still describes the full run: the publish command line and the guarded
            // archive side effect reported as conditional.
            await Assert.That(console).Contains("build: dotnet publish");
            await Assert.That(console).Contains("conditional: output folder");
            await Assert.That(console).Contains("the dry run wrote nothing");
        });
    }

    [Test]
    public async Task The_build_dry_run_prints_the_command_line_the_real_run_executes()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Alpha/Alpha.csproj", "kinds": ["executables"] } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "publish": { "sources": [] } }""");
        _invoker.OnInvocation = RecordingProcessInvoker.MaterializePublishOutput;

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            // The dry run runs through the request surface with the test's own line sink, so
            // no console wrapping stands between the plan and the assertion.
            List<string> plannedLines = [];
            await _buildStageRunner.RunAsync(
                new BuildStageRequest
                {
                    Manifest = _manifestLoader.LoadFromDirectory(new DirectoryInfo(workspace.Root)),
                    ManifestDirectory = new DirectoryInfo(workspace.Root),
                    Preset = BuildPreset.None,
                    OutputRoot = new DirectoryInfo(Path.Combine(workspace.Root, "release")),
                    WriteLine = plannedLines.Add,
                    RunMode = StageRunMode.DryRun,
                },
                CancellationToken.None);

            int exitCode = await CliHarness.InvokeAsync(
                new BuildCommand(_manifestLoader, _buildStageRunner), "build");

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(1);

            // The dry-run plan carries exactly the argument list the real run executed, so
            // both modes read one plan.
            string executed =
                $"build: dotnet {string.Join(' ', _invoker.Invocations[0].ArgumentList)}";
            await Assert.That(plannedLines).Contains(executed);
        });
    }

    /// <summary>
    /// One fake external command: a spawn-shaped record whose process the recording invoker
    /// catches, so both modes can be asserted without a real toolchain.
    /// </summary>
    /// <param name="diagnosis">An optional diagnosis callback the record carries.</param>
    private static PlannedCommand ProcessCommand(Func<string>? diagnosis = null) =>
        new()
        {
            CommandLine = "stage: dotnet publish src/App/App.csproj -c Release",
            TargetFilePath = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
            ArgumentList = ["publish", "src/App/App.csproj", "-c", "Release"],
            WorkingDirectory = Path.GetTempPath(),
            ArtifactPaths = ["release/App-win-x64"],
            Diagnosis = diagnosis,
        };
}
