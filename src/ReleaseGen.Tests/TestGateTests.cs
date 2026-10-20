namespace ReleaseGen.Tests;

using Microsoft.Extensions.DependencyInjection;
using ReleaseGenCli.Commands;
using ReleaseGenLib.Models;

/// <summary>
/// The test gate and the chained run's fail-fast ordering: a declared test
/// section with a nonzero exit blocks publish before any network call, an absent section warns
/// and proceeds, a standalone publish consults the gate by running the test stage itself, and a
/// failed test stage aborts the chain before publish.
/// </summary>
public class TestGateTests
{
    [Test]
    public async Task A_declared_section_with_exit_zero_passes_the_gate()
    {
        TestGateResult gate = TestGateResult.FromStage(exitCode: 0, hasTestSection: true);

        await Assert.That(gate.Outcome).IsEqualTo(TestGateOutcome.Passed);
        await Assert.That(gate.ExitCode).IsNull();
    }

    [Test]
    public async Task A_declared_section_with_a_nonzero_exit_blocks_the_gate_carrying_the_code()
    {
        TestGateResult gate = TestGateResult.FromStage(exitCode: 7, hasTestSection: true);

        await Assert.That(gate.Outcome).IsEqualTo(TestGateOutcome.Blocked);
        await Assert.That(gate.ExitCode).IsEqualTo(7);
    }

    [Test]
    public async Task An_absent_section_lands_on_the_warning_path_even_after_a_failed_stage()
    {
        TestGateResult gate = TestGateResult.FromStage(exitCode: 3, hasTestSection: false);

        await Assert.That(gate.Outcome).IsEqualTo(TestGateOutcome.NoSection);
        await Assert.That(gate.ExitCode).IsNull();
    }

    [Test]
    public async Task A_blocked_gate_stops_publish_before_any_push()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source, declareTestSection: true));
        StagePackage(workspace);
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();
            StageContext.TestGate = new TestGateResult(TestGateOutcome.Blocked, 3);

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new PublishCommand(services), "publish", "--api-key", "local-key");

                    await Assert.That(exitCode).IsEqualTo(1);
                });

                await Assert.That(console).Contains("blocked by the test gate");
                await Assert.That(invoker.Invocations.Count).IsEqualTo(0); // No push, no network.
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task An_absent_test_section_warns_and_publishes_anyway()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source, declareTestSection: false));
        StagePackage(workspace);
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new PublishCommand(services), "publish", "--api-key", "local-key");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // An absent section is the opt-in default - a warning, never a failure.
                await Assert.That(console).Contains("warning:");
                await Assert.That(console).Contains("no test section in the manifest");
                await Assert.That(invoker.Invocations.Count).IsEqualTo(1);
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_standalone_publish_blocks_when_its_gate_consult_fails()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source, declareTestSection: true));
        StagePackage(workspace);
        RecordingProcessInvoker invoker = new()
        {
            ExitCodeFactory = configuration =>
                string.Equals(configuration.ArgumentList[0], "test", StringComparison.Ordinal) ? 1 : 0,
        };

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(
                        new PublishCommand(services), "publish", "--api-key", "local-key");

                    await Assert.That(exitCode).IsEqualTo(1);
                });

                // The gate consumes the test stage's own exit code, so a standalone
                // publish cannot bypass a declared test section.
                await Assert.That(console).Contains("running the test stage");
                await Assert.That(invoker.Invocations.Count).IsEqualTo(1);
                await Assert.That(invoker.Invocations[0].ArgumentList[0]).IsEqualTo("test");
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_standalone_publish_pushes_when_its_gate_consult_passes()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source, declareTestSection: true));
        StagePackage(workspace);
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(services), "publish", "--api-key", "local-key");

                await Assert.That(exitCode).IsEqualTo(0);
                await Assert.That(invoker.Invocations.Count).IsEqualTo(2);
                await Assert.That(invoker.Invocations[0].ArgumentList[0]).IsEqualTo("test");
                await Assert.That(invoker.Invocations[1].ArgumentList[0]).IsEqualTo("nuget");
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_failed_test_stage_aborts_the_chain_before_publish()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(ChainManifest(source));
        StagePackage(workspace);
        RecordingProcessInvoker invoker = new()
        {
            ExitCodeFactory = configuration =>
                string.Equals(configuration.ArgumentList[0], "test", StringComparison.Ordinal) ? 1 : 0,
        };

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(new RunCommand(services), "run");

                    await Assert.That(exitCode).IsEqualTo(1);
                });

                await Assert.That(console).Contains("the test stage failed");
                await Assert.That(console).Contains("aborting the run before the remaining stages");

                // Fail-fast: package and publish never ran, so the staged package was
                // never pushed and the publish stage never consulted the gate.
                await Assert.That(invoker.Invocations.Count).IsEqualTo(1);
                await Assert.That(invoker.Invocations[0].ArgumentList[0]).IsEqualTo("test");
                await Assert.That(StageContext.TestGate).IsNull();
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    [Test]
    public async Task A_chained_run_notes_the_stages_the_manifest_did_not_name()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """
            {
              "targets": [],
              "build": { "runtimeIdentifiers": [] },
              "publish": { "sources": [] }
            }
            """);
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(new RunCommand(services), "run");

                    await Assert.That(exitCode).IsEqualTo(0);
                });

                // Skip notes: the executed stage set stays honest on the console.
                await Assert.That(console).Contains("skipping the test stage");
                await Assert.That(console).Contains("the manifest declares no test section");
                await Assert.That(console).Contains("skipping the package stage");
                await Assert.That(console).Contains("skipping the publish stage");
                await Assert.That(invoker.Invocations.Count).IsEqualTo(0);
            }
            finally
            {
                StageContext.Reset();
            }
        });
    }

    /// <summary>
    /// Stages the package stage's local product the publish stage enumerates, so a push would
    /// happen if the gate let one through.
    /// </summary>
    /// <param name="workspace">The workspace staging the package.</param>
    private static void StagePackage(TestWorkspace workspace) =>
        workspace.WriteFile(
            Path.Combine("release", "packages", "lib", "Lib", "Lib.1.0.0.nupkg"), "staged package");

    private static string PackageManifest(string source, bool declareTestSection)
    {
        string testSection = declareTestSection ? """ "test": { "projects": [] }, """ : string.Empty;

        return $$"""
        {
          "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ],
          {{testSection}} "publish": { "sources": ["{{JsonPath(source)}}"] }
        }
        """;
    }

    /// <summary>
    /// A chain manifest: declared test section, package kind, but no configured rid, so the
    /// build stage reports nothing to publish without invoking anything and the chain reaches
    /// the test stage immediately.
    /// </summary>
    /// <param name="source">The local publish source.</param>
    private static string ChainManifest(string source) =>
        $$"""
        {
          "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ],
          "build": { "runtimeIdentifiers": [] },
          "test": { "projects": [] },
          "publish": { "sources": ["{{JsonPath(source)}}"] }
        }
        """;

    /// <summary>
    /// Renders a file-system path JSON-string-safe: Windows backslashes would otherwise read as
    /// escape sequences in the manifest text.
    /// </summary>
    /// <param name="path">The path to embed in manifest JSON.</param>
    private static string JsonPath(string path) => path.Replace('\\', '/');
}
