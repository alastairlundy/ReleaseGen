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
/// fixture-driven scanner legs hand the stage recorded push output - clearly labeled
/// synthetic duplicate-marker lines - to pin the strict both-stream marker scan and the
/// stderr-side note reclassification with no network and no nuget.org run.
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

            // The redacted pushed command line shows the switch joined the real argument
            // list; the credential fragment names whichever key channel this machine's SDK
            // resolved to - the environment on 10.0.300+, the argument on older SDKs.
            await Assert.That(
                console.Contains("--api-key **** --skip-duplicate") ||
                console.Contains(
                    "(api key via the NUGET_API_KEY environment variable) --skip-duplicate"))
                .IsTrue();
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

            RecordingProcessInvoker invoker = new()
            {
                OutputFactory = _ => (SyntheticStdoutSkipFixture, string.Empty),
            };

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(_manifestLoader, invoker), "publish", "--api-key", "local-key");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // The strict scan counts a marker-carrying push as skipped and never as pushed,
            // the summary reports the two totals separately, and the manifest channel
            // appended --skip-duplicate to the argument list.
            await Assert.That(console).Contains("pushed 0 package(s)");
            await Assert.That(console).Contains("skipped 1 package(s)");
            await Assert.That(Compact(console)).Contains("note:" + Compact(SyntheticSkipMarkerLine));
            // The SDK probe runs before the first push; the push itself carries the switch.
            await Assert.That(invoker.Invocations.Count).IsEqualTo(2);
            await Assert.That(string.Join(' ', invoker.Invocations[1].ArgumentList))
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

            // The fixture set's stderr-side marker line: counting must not depend on which
            // stream dotnet chose for the notice.
            RecordingProcessInvoker invoker = new()
            {
                OutputFactory = _ => (string.Empty, SyntheticStderrSkipFixture),
            };

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(_manifestLoader, invoker), "publish", "--api-key", "local-key");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // Reclassification is exact, not blanket: the stderr marker line leaves the
            // warning path and re-emits as a stage note, while a non-marker stderr line
            // keeps its warning tone; the skipped push names its package and source.
            string compact = Compact(console);
            await Assert.That(console).Contains("pushed 0 package(s)");
            await Assert.That(console).Contains("skipped 1 package(s)");
            await Assert.That(compact).Contains("note:" + Compact(SyntheticSkipMarkerLine));
            await Assert.That(compact.Contains("warning:" + Compact(SyntheticSkipMarkerLine))).IsFalse();
            await Assert.That(compact).Contains("warning:" + Compact(SyntheticNonMarkerStderrLine));
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

            RecordingProcessInvoker invoker = new()
            {
                OutputFactory = _ => (SyntheticLookalikeStdoutFixture, string.Empty),
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
    /// SYNTHETIC fixture text, clearly labeled as such: these lines are NOT captured
    /// <c>dotnet nuget push</c> output. They stand in for the duplicate notice so the marker
    /// scan is pinned in CI, which runs no nuget.org pushes. A follow-up fixture capture
    /// replaces them with real captured output - including which stream the notice arrived
    /// on - and reconciles <see cref="PublishCommand.SkipDuplicateMarker"/> against it if
    /// the live wording has drifted.
    /// </summary>
    private const string SyntheticSkipMarkerLine =
        "Package 'release/packages/lib/Local/ReleaseGen.LocalPush.1.0.0.nupkg' already exists at feed 'local-source'.";

    /// <summary>The stdout-side fixture: a pushing header line plus the marker line.</summary>
    private const string SyntheticStdoutSkipFixture =
        "Pushing ReleaseGen.LocalPush.1.0.0.nupkg to 'local-source'...\n" + SyntheticSkipMarkerLine;

    /// <summary>
    /// A non-marker stderr line dotnet really prints for this push shape: it keeps the
    /// warning path, which is what makes the marker line's reclassification selective.
    /// </summary>
    private const string SyntheticNonMarkerStderrLine =
        "warn : The option to skip duplicates is not currently supported for this type of push.";

    /// <summary>The stderr-side fixture: the non-marker warning line plus the marker line.</summary>
    private const string SyntheticStderrSkipFixture =
        SyntheticNonMarkerStderrLine + "\n" + SyntheticSkipMarkerLine;

    /// <summary>
    /// A drifted-wording lookalike: "in feed" is not the pinned "at feed" marker, so the
    /// strict scan must not count it.
    /// </summary>
    private const string SyntheticLookalikeStdoutFixture =
        "Package 'release/packages/lib/Local/ReleaseGen.LocalPush.1.0.0.nupkg' already exists in feed 'local-source'.";

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
