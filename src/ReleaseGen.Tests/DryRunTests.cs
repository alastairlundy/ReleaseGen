using Microsoft.Extensions.DependencyInjection;
using ReleaseGen.Cli.Commands;
using ReleaseGen.Cli.Helpers;

namespace ReleaseGen.Tests;

using ReleaseGenLib.Abstractions;

/// <summary>
/// The <c>--dry-run</c> flag on every stage command and the chained run: each stage prints its
/// would-be actions through the shared planned record, nothing is written anywhere (including
/// the <c>release/</c> tree and <c>TestResults/</c>), the publish stage prints the would-be
/// gate consult instead of running tests, and the exit code reflects manifest validation
/// errors only - a zero-exit dry run is a plan, not a certification. The build stage's own
/// plan assertions live with the planned-command record in <see cref="PlannedCommandTests"/>;
/// these tests cover the flag's per-stage surface.
/// </summary>
[ReleaseGenDi]
public class DryRunTests
{
    private readonly RecordingProcessInvoker _invoker;
    private readonly IManifestLoader _manifestLoader;
    private readonly IBuildStageRunner _buildStageRunner;

    /// <summary>
    /// The <c>--dry-run</c> flag on every stage command and the chained run: each stage prints
    /// its would-be actions through the shared planned record, nothing is written anywhere, the
    /// publish stage prints the would-be gate consult instead of running tests, and the exit
    /// code reflects manifest validation errors only.
    /// </summary>
    public DryRunTests(ServiceProvider services, RecordingProcessInvoker invoker)
    {
        _invoker = invoker;
        _manifestLoader = services.GetRequiredService<IManifestLoader>();
        _buildStageRunner = services.GetRequiredService<IBuildStageRunner>();
    }

    [Test]
    public async Task A_dry_run_test_stage_prints_the_plan_without_running_tests()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "test": { "projects": ["src/Lib.Tests/Lib.Tests.csproj"] }, "publish": { "sources": [] } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new TestCommand(_manifestLoader, _invoker), "test", "--dry-run");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // The plan names the invocation a real run would spawn; nothing executed, so no
            // TestResults/ tree appeared either. Spectre wraps long lines, so the absolute
            // path is matched on a whitespace-free copy of the console.
            string compact = string.Concat(console.Where(static c => !char.IsWhiteSpace(c)));
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
            await Assert.That(console).Contains("test: dotnet test");
            await Assert.That(compact).Contains(Path.Combine("src", "Lib.Tests", "Lib.Tests.csproj"));
            await Assert.That(console).Contains("no tests ran and no TestResults/ was written");
            await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "TestResults"))).IsFalse();
            await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
        });
    }

    [Test]
    public async Task A_dry_run_package_stage_prints_the_plan_without_writing()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "publish": { "sources": [] } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new PackageCommand(_manifestLoader, _invoker), "package", "--dry-run");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // The plan prints the pack a real run would spawn and its output location, and
            // writes nothing under release/.
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
            await Assert.That(console).Contains("package: dotnet pack");
            await Assert.That(console).Contains("nothing was written under");
            await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
        });
    }

    [Test]
    public async Task A_dry_run_publish_prints_the_gate_consult_and_the_would_be_pushes()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "test": { "projects": ["src/Lib.Tests/Lib.Tests.csproj"] }, "publish": { "sources": [] } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    // No API key: a plan pushes nothing, so a missing key is reported rather
                    // than failed, and the exit code stays manifest-validation only.
                    int exitCode = await CliHarness.InvokeAsync(
                        new PublishCommand(_manifestLoader, _invoker), "publish", "--dry-run");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // The gate consult names the tests a real run would start instead of starting
                // them, and the pushes print as redacted plan lines. Spectre wraps long lines,
                // so the project path is matched on a whitespace-free copy.
                string compact = string.Concat(console.Where(static c => !char.IsWhiteSpace(c)));
                await Assert.That(console).Contains("tests would run for:");
                await Assert.That(compact).Contains("src/Lib.Tests/Lib.Tests.csproj");
                await Assert.That(console).Contains("dotnet nuget push");
                await Assert.That(console).Contains("--api-key ****");
                await Assert.That(console).Contains("plan, not a certification");

                // Nothing ran: no test-stage consult, no push, and no results tree.
                await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
                await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "TestResults"))).IsFalse();
                await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_dry_run_chained_run_plans_every_named_stage_and_writes_nothing()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["executables", "lib-package"] } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "test": { "projects": ["src/Lib.Tests/Lib.Tests.csproj"] }, "publish": { "sources": [] } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new RunCommand(_manifestLoader, _invoker, _buildStageRunner), "run", "--dry-run");

                    // A clean plan exits 0: the chained run's exit code reflects manifest
                    // validation errors only.
                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // The flag forwards to every stage the manifest names, so the chain reports
                // each stage's would-be actions in manifest order.
                await Assert.That(console).Contains("run: dry run: chained plan starting");
                await Assert.That(console).Contains("build: dotnet publish");
                await Assert.That(console).Contains("test: dotnet test");
                await Assert.That(console).Contains("package: dotnet pack");
                await Assert.That(console).Contains("tests would run for:");
                await Assert.That(console).Contains("publish: dotnet nuget push");
                await Assert.That(console).Contains("nothing was written anywhere");

                await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
                await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
                await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "TestResults"))).IsFalse();
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_dry_run_still_fails_on_a_manifest_validation_error()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        // The renamed-away 'archive' key fails the strict parse: a manifest error is the one
        // failure a dry run still reports with a nonzero exit.
        workspace.WriteManifest(
            """{ "targets": [], "build": { "runtimeIdentifiers": [] }, "publish": { "sources": [] }, "archive": "zip" }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner), "build", "--dry-run");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            await Assert.That(console).Contains("error:");
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
            await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
        });
    }
}
