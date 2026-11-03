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
    public async Task A_dry_run_publish_prints_the_resolved_skip_mode_when_the_manifest_enables_skipping()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "publish": { "sources": ["local-feed"], "skipDuplicates": true } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    // No API key: a plan pushes nothing, so the missing key is reported
                    // rather than failed, and the exit code stays manifest-validation only.
                    int exitCode = await CliHarness.InvokeAsync(
                        new PublishCommand(_manifestLoader, _invoker), "publish", "--dry-run");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // The plan states the resolved skip mode a real run would push under, and
                // the would-be push carries the switch that decision implies; an absent CLI
                // channel is never a conflict (ledger #T017, #D003).
                await Assert.That(console).Contains(
                    "resolved skip mode: duplicates skipped; the push would pass --skip-duplicate.");
                await Assert.That(console).Contains("--api-key **** --skip-duplicate");
                await Assert.That(console.Contains("skipDuplicates conflict")).IsFalse();

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
    public async Task A_dry_run_publish_prints_the_fail_fast_mode_when_no_channel_declares_skipping()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "publish": { "sources": ["local-feed"] } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new PublishCommand(_manifestLoader, _invoker), "publish", "--dry-run");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // No channel declared skipping, so the plan rehearses today's fail-fast
                // default: the mode line tracks the resolved decision rather than carrying
                // fixed wording (ledger #T017, #T007 default false).
                await Assert.That(console).Contains(
                    "resolved skip mode: duplicates fail the stage; no --skip-duplicate would be passed.");

                await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
                await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_dry_run_publish_prints_one_conflict_line_when_the_channels_differ_and_exits_0()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        // Both channels explicit and differing (manifest false, switch true): the CLI value
        // wins, and the plan prints the same conflict line a real run would (ledger #T017,
        // #D003, #T008 at-most-once).
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "publish": { "sources": ["local-feed"], "skipDuplicates": false } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new PublishCommand(_manifestLoader, _invoker),
                        "publish",
                        "--dry-run",
                        "--skip-duplicate");

                    // The conflict is a report, not a failure: the plan's exit code stays
                    // manifest-validation only (ledger #T017).
                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // Exactly one conflict line through the error channel - the standard-error
                // surface a real run prints it on - and the mode line shows the CLI winner
                // the plan resolved to (ledger #T017, #D003).
                await Assert.That(CountOccurrences(console, SkipConflictLine)).IsEqualTo(1);
                await Assert.That(console).Contains(
                    "resolved skip mode: duplicates skipped; the push would pass --skip-duplicate.");

                await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
                await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_chained_dry_run_forwards_skip_duplicate_to_the_publish_stage()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        // The manifest never declares the key, so the plan can only show skipping if the
        // chain forwarded the bare switch to the publish stage (ledger #D003).
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "publish": { "sources": ["local-feed"] } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new RunCommand(_manifestLoader, _invoker, _buildStageRunner),
                        "run",
                        "--dry-run",
                        "--skip-duplicate");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // The forwarded switch resolved the publish stage's decision exactly as a
                // separate publish invocation would: skip-enabled mode, the switch in the
                // planned push, and no conflict from the absent manifest channel.
                await Assert.That(console).Contains(
                    "resolved skip mode: duplicates skipped; the push would pass --skip-duplicate.");
                await Assert.That(console).Contains("--api-key **** --skip-duplicate");
                await Assert.That(console.Contains("skipDuplicates conflict")).IsFalse();

                await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
                await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release"))).IsFalse();
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_chained_dry_run_forwards_an_explicit_false_skip_value_as_a_conflict()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        // Both channels explicit and differing (manifest true, forwarded false): if the
        // chain had dropped the value, the manifest would win silently instead - so the
        // conflict line plus the fail-fast mode line together prove "false" survived the
        // hop as its own channel (ledger #D003).
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ], "publish": { "sources": ["local-feed"], "skipDuplicates": true } }""");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new RunCommand(_manifestLoader, _invoker, _buildStageRunner),
                        "run",
                        "--dry-run",
                        "--skip-duplicate",
                        "false");

                    // The conflict is a report, not a failure: the chain's exit code stays
                    // manifest-validation only (ledger #T017).
                    await Assert.That(exitCode).IsEqualTo(0);
                });

                await Assert.That(CountOccurrences(console, SkipConflictLine)).IsEqualTo(1);
                await Assert.That(console).Contains(
                    "resolved skip mode: duplicates fail the stage; no --skip-duplicate would be passed.");
                await Assert.That(console.Contains("--api-key **** --skip-duplicate")).IsFalse();

                await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
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

    /// <summary>
    /// The exact conflict line the publish stage prints through the error channel when both
    /// skip channels carry explicit differing values - the wording the dry-run plan mirrors
    /// from the real run, so the tests pin the whole line rather than a fragment of it
    /// (ledger #T017, #D003).
    /// </summary>
    private const string SkipConflictLine =
        "error: skipDuplicates conflict: --skip-duplicate (command line) wins over publish.skipDuplicates";

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="value"/>, so a test can assert a
    /// message printed exactly once rather than at least once (the at-most-once conflict cap
    /// of ledger #T008, #T017).
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
