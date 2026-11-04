using CliInvoke.Core;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGen.Cli.Commands;
using ReleaseGen.Cli.Helpers;
using ReleaseGenLib.Abstractions;

namespace ReleaseGen.Tests;

/// <summary>
/// The publish stage's integration legs, each one a stage-mutating test serialized behind
/// <see cref="TestWorkspace.InDirectoryAsync"/> because stage commands discover the manifest
/// from the current directory. The real-invoker leg runs a real <c>dotnet nuget push</c> into
/// a local folder source instead of nuget.org: a normal push lands the package, and the
/// double-push case documents that a local source judges re-pushes its own way. The
/// fixture-driven scanner legs hand the stage recorded push output - the checked-in
/// capture of a real nuget.org duplicate push (Fixtures/nuget-push-duplicate.txt, whose
/// notice arrived on stdout) with the stderr-side stream placement clearly labeled as
/// constructed - to pin the strict both-stream marker scan and the stderr-side note
/// reclassification with no network and no nuget.org run.
/// Composed from the production container via <see cref="ReleaseGenRealInvokerDiAttribute"/>,
/// so the real-invoker leg runs the unmodified production composition.
/// </summary>
[ReleaseGenRealInvokerDi]
public class PublishIntegrationTests
{
    private readonly IManifestLoader _manifestLoader;
    private readonly IProcessInvoker _processInvoker;

    public PublishIntegrationTests(ServiceProvider services)
    {
        _manifestLoader = services.GetRequiredService<IManifestLoader>();
        _processInvoker = services.GetRequiredService<IProcessInvoker>();
    }

    [Test]
    public async Task Integration_a_real_push_lands_a_package_in_a_local_source()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source, project: "Local"));
        TestPackage.WriteNupkg(
            Path.Combine(workspace.Root, "release", "packages", "lib", "Local", "ReleaseGen.LocalPush.1.0.0.nupkg"),
            "ReleaseGen.LocalPush",
            "1.0.0");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            // The real invoker runs dotnet nuget push against the local folder source - never
            // nuget.org.
            int exitCode = await CliHarness.InvokeAsync(
                new PublishCommand(_manifestLoader, _processInvoker), "publish", "--api-key", "local-key");

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(File.Exists(
                Path.Combine(source, "ReleaseGen.LocalPush.1.0.0.nupkg"))).IsTrue();
        });
    }

    [Test]
    public async Task A_local_folder_source_pushed_twice_exits_0_with_pushed_2_and_skipped_0()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");

        // One invocation declares the same local-folder source twice, so the staged package
        // is pushed to it twice: a re-push a local source judges its own way. No skip marker
        // ever appears on this path - dotnet reports duplicates only for server-backed feeds -
        // so both pushes count as pushed and none as skipped, at exit 0 (an exit-0 push
        // without the marker counts as pushed; sources judge duplicates themselves, so no
        // cross-source guarantee exists - a nuget.org duplicate would have skipped instead).
        workspace.WriteManifest(DoubleDeclaredSourceManifest(source));
        TestPackage.WriteNupkg(
            Path.Combine(workspace.Root, "release", "packages", "lib", "Local", "ReleaseGen.LocalPush.1.0.0.nupkg"),
            "ReleaseGen.LocalPush",
            "1.0.0");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                // The switch rides the real push; nothing queries the source first - it
                // alone judges the re-push.
                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(_manifestLoader, _processInvoker),
                    "publish",
                    "--api-key",
                    "local-key",
                    "--skip-duplicate");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            await Assert.That(console).Contains("pushed 2 package(s)");
            await Assert.That(console).Contains("skipped 0 package(s)");

            // The redacted pushed command line shows the switch joined the real argument list.
            await Assert.That(console).Contains("--api-key **** --skip-duplicate");
        });
    }

    [Test]
    public async Task A_push_whose_stdout_carries_the_skip_marker_counts_as_skipped()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(SkipEnabledPackageManifest(source));
        StagePackage(workspace, "ReleaseGen.LocalPush.1.0.0.nupkg");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            // The real captured stdout of a nuget.org duplicate push (409 Conflict, exit 0):
            // the strict scan must count it as skipped on the evidence itself, not on
            // restated synthetic text.
            RecordingProcessInvoker invoker = new()
            {
                OutputFactory = _ => (CapturedStdout(), string.Empty),
            };

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(_manifestLoader, invoker), "publish", "--api-key", "local-key");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // The strict scan counts a marker-carrying push as skipped and never as pushed,
            // the summary reports the two totals separately, the captured notice re-emits
            // as a note, and the manifest channel appended --skip-duplicate to the argument
            // list. The capture names ReleaseGen 0.3.0 while the staged package differs:
            // the scan keys on the marker text, never on the package name.
            await Assert.That(console).Contains("pushed 0 package(s)");
            await Assert.That(console).Contains("skipped 1 package(s)");
            await Assert.That(console).Contains("skipped package 'ReleaseGen.LocalPush.1.0.0.nupkg'");
            await Assert.That(console).Contains("the source declared it a duplicate");
            await Assert.That(Compact(console)).Contains(JsonPath(source));
            await Assert.That(Compact(console)).Contains("note:" + Compact(CapturedMarkerLine()));
            await Assert.That(invoker.Invocations.Count).IsEqualTo(1);
            await Assert.That(string.Join(' ', invoker.Invocations[0].ArgumentList))
                .Contains("--skip-duplicate");
        });
    }

    [Test]
    public async Task A_push_whose_stderr_carries_the_skip_marker_reemits_the_line_as_a_note()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(SkipEnabledPackageManifest(source));
        StagePackage(workspace, "ReleaseGen.LocalPush.1.0.0.nupkg");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            // T015's stderr-side match: the entire captured stdout re-delivered on stderr.
            // The real capture observed the notice on stdout with stderr empty, so this
            // stream placement is constructed - counting must not depend on which stream
            // dotnet chose for the notice.
            RecordingProcessInvoker invoker = new()
            {
                OutputFactory = _ => (string.Empty, CapturedStdout()),
            };

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(_manifestLoader, invoker), "publish", "--api-key", "local-key");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // Reclassification is exact, not blanket: the stderr marker line leaves the
            // warning path and re-emits as a stage note, while a captured non-marker line
            // keeps its warning tone; the skipped push names its package and source.
            string compact = Compact(console);
            await Assert.That(console).Contains("pushed 0 package(s)");
            await Assert.That(console).Contains("skipped 1 package(s)");
            await Assert.That(compact).Contains("note:" + Compact(CapturedMarkerLine()));
            await Assert.That(compact.Contains("warning:" + Compact(CapturedMarkerLine()))).IsFalse();
            await Assert.That(compact).Contains("warning:" + Compact(CapturedNonMarkerLine()));
            await Assert.That(console).Contains("skipped package 'ReleaseGen.LocalPush.1.0.0.nupkg'");
            await Assert.That(console).Contains("the source declared it a duplicate");
            await Assert.That(compact).Contains(JsonPath(source));
        });
    }

    [Test]
    public async Task A_marker_lookalike_line_counts_as_pushed_rather_than_skipped()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(SkipEnabledPackageManifest(source));
        StagePackage(workspace, "ReleaseGen.LocalPush.1.0.0.nupkg");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();

            // The lookalike is derived from the captured notice with the marker's decisive
            // word drifted ("in feed" for "at feed"), so the strict scan's refusal is
            // pinned against evidence-adjacent text, not an invented string.
            RecordingProcessInvoker invoker = new()
            {
                OutputFactory = _ => (CapturedMarkerLine().Replace("at feed", "in feed", StringComparison.Ordinal), string.Empty),
            };

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(_manifestLoader, invoker), "publish", "--api-key", "local-key");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // Strict means strict: wording drift undercounts the skipped total rather than
            // inventing a skip, so an exit-0 push whose output lacks the
            // exact marker still counts as pushed.
            await Assert.That(console).Contains("pushed 1 package(s)");
            await Assert.That(console).Contains("skipped 0 package(s)");
        });
    }

    /// <summary>
    /// The evidence pin for the constant and its stream (#T009, #T015): the captured
    /// payload must carry <see cref="PublishCommand.SkipDuplicateMarker"/> verbatim, and
    /// the fixture must record which stream the notice arrived on - stdout for this
    /// capture. Wording drift fails here instead of hiding behind a loose match.
    /// </summary>
    [Test]
    public async Task The_capture_fixture_pins_the_marker_constant_and_records_its_stream()
    {
        await Assert.That(CapturedStdout()).Contains(PublishCommand.SkipDuplicateMarker);
        await Assert.That(FixtureText()).Contains("# stream: stdout");
    }

    /// <summary>
    /// The checked-in capture of a real duplicate push (Fixtures/nuget-push-duplicate.txt):
    /// the push step's stdout payload with the provenance header's comment and blank lines
    /// stripped. The header records the capture's stream evidence - the notice arrived on
    /// stdout with stderr empty.
    /// </summary>
    private static string CapturedStdout() =>
        string.Join('\n', File.ReadAllLines(FixturePath)
            .Where(static line => line.Length > 0 && !line.StartsWith('#')));

    /// <summary>
    /// The captured notice line itself - the evidence <see cref="PublishCommand.SkipDuplicateMarker"/>
    /// must match. Fails with a reconciliation pointer when the constant has drifted off
    /// the capture (#T009).
    /// </summary>
    private static string CapturedMarkerLine()
    {
        string? markerLine = CapturedStdout().Split('\n').FirstOrDefault(
            static line => line.Contains(PublishCommand.SkipDuplicateMarker, StringComparison.Ordinal));

        return markerLine ?? throw new InvalidOperationException(
            $"The captured fixture carries no line matching '{PublishCommand.SkipDuplicateMarker}'; " +
            "reconcile the constant with src/ReleaseGen.Tests/Fixtures/nuget-push-duplicate.txt.");
    }

    /// <summary>
    /// A captured progress line that is not the notice, for proving a non-marker line
    /// keeps the warning tone when the capture is replayed from stderr.
    /// </summary>
    private static string CapturedNonMarkerLine() =>
        CapturedStdout().Split('\n').First(
            static line => !line.Contains(PublishCommand.SkipDuplicateMarker, StringComparison.Ordinal));

    /// <summary>The full fixture text, provenance header included, for the stream-record pin.</summary>
    private static string FixtureText() => File.ReadAllText(FixturePath);

    /// <summary>
    /// Absolute path to the checked-in capture: absolute so the stage-mutating tests find
    /// it even while <see cref="TestWorkspace.InDirectoryAsync"/> holds a different cwd.
    /// </summary>
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nuget-push-duplicate.txt");

    /// <summary>
    /// The captured console with all whitespace removed, so an assertion on a line survives
    /// Spectre's wrapping - the pattern the other suites use for long lines.
    /// </summary>
    private static string Compact(string text) =>
        string.Concat(text.Where(static c => !char.IsWhiteSpace(c)));

    /// <summary>
    /// Stages a package file where the package stage would have written it; the scanner legs
    /// never spawn a push, so a placeholder body is enough.
    /// </summary>
    /// <returns>The staged package's full path.</returns>
    private static string StagePackage(TestWorkspace workspace, string fileName) =>
        workspace.WriteFile(
            Path.Combine("release", "packages", "lib", "Local", fileName), "staged package");

    private static string PackageManifest(string source, string project) =>
        $$"""
        {
          "targets": [ { "project": "src/{{project}}/{{project}}.csproj", "kinds": ["lib-package"] } ],
          "publish": { "sources": ["{{JsonPath(source)}}"] }
        }
        """;

    /// <summary>
    /// A manifest whose publish section declares skipping, so the resolved decision appends
    /// <c>--skip-duplicate</c> to the recorded push without a CLI switch.
    /// </summary>
    private static string SkipEnabledPackageManifest(string source) =>
        $$"""
        {
          "targets": [ { "project": "src/Local/Local.csproj", "kinds": ["lib-package"] } ],
          "publish": { "sources": ["{{JsonPath(source)}}"], "skipDuplicates": true }
        }
        """;

    /// <summary>
    /// A manifest declaring the same local-folder source twice, so one invocation pushes the
    /// staged package to it twice - the double-push the local-source case documents.
    /// </summary>
    private static string DoubleDeclaredSourceManifest(string source) =>
        $$"""
        {
          "targets": [ { "project": "src/Local/Local.csproj", "kinds": ["lib-package"] } ],
          "publish": { "sources": ["{{JsonPath(source)}}", "{{JsonPath(source)}}"] }
        }
        """;

    private static string JsonPath(string path) => path.Replace('\\', '/');
}
