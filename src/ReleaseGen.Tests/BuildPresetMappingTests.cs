using ReleaseGenLib.Exceptions;

namespace ReleaseGen.Tests;

using ReleaseGenCli.Commands;
using ReleaseGenLib;
using ReleaseGenLib.Models;

/// <summary>
/// The build preset and flag mapping: each of the four shipped presets plus
/// none resolves to its documented dotnet publish switch set, manifest keys override the preset
/// defaults tri-state, <c>--preset</c> beats the manifest key for one invocation without ever
/// writing the file, and an unknown preset is refused naming the known set. The four
/// command-level tests resolve the stage services and the recording invoker from the test's
/// own DI scope; the switch-mapping tests are pure and need neither.
/// </summary>
[ReleaseGenDi]
public class BuildPresetMappingTests(IServiceProvider services, RecordingProcessInvoker invoker)
{
    [Test]
    public async Task Aot_preset_maps_to_publish_aot_and_self_contained()
    {
        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.AotPlusTrimming, new ManifestBuildSection(), ArchiveFormats.Zip, isWindows: true);

        // AOT implies trimming, so no separate trimming switch rides along.
        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishAot=true | --self-contained=true | -p:PublishSingleFile=true");
    }

    [Test]
    public async Task Ready_to_run_preset_maps_to_ready_to_run_and_self_contained()
    {
        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.ReadyToRun, new ManifestBuildSection(), ArchiveFormats.Zip, isWindows: true);

        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishReadyToRun=true | --self-contained=true | -p:PublishSingleFile=true");
    }

    [Test]
    public async Task Trimming_only_preset_maps_to_trimming_and_self_contained()
    {
        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.TrimmingOnly, new ManifestBuildSection(), ArchiveFormats.Zip, isWindows: true);

        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishTrimmed=true | --self-contained=true | -p:PublishSingleFile=true");
    }

    [Test]
    public async Task Framework_dependent_preset_maps_to_roll_forward_and_no_self_containment()
    {
        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.FrameworkDependent, new ManifestBuildSection(), ArchiveFormats.Zip, isWindows: true);

        // Minor covers patch too: RollForward=Minor rolls forward within the major for minor and
        // patch releases.
        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "--self-contained=false | -p:RollForward=Minor | -p:PublishSingleFile=true");
    }

    [Test]
    public async Task Preset_none_contributes_no_implicit_switches()
    {
        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.None, new ManifestBuildSection(), ArchiveFormats.Zip, isWindows: true);

        await Assert.That(Render(switches.ToArguments())).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Single_file_defaults_on_unix_even_without_a_zip_archive()
    {
        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.TrimmingOnly, new ManifestBuildSection(), ArchiveFormats.None, isWindows: false);

        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishTrimmed=true | --self-contained=true | -p:PublishSingleFile=true");
    }

    [Test]
    public async Task Single_file_stays_off_on_windows_when_the_target_is_not_zip_archived()
    {
        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.TrimmingOnly, new ManifestBuildSection(), ArchiveFormats.None, isWindows: true);

        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishTrimmed=true | --self-contained=true");
    }

    [Test]
    public async Task Manifest_keys_override_the_preset_defaults()
    {
        ManifestBuildSection section = new()
        {
            PublishAot = true,
            PublishTrimmed = true,
            PublishReadyToRun = true,
            SelfContained = false,
        };

        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.None, section, ArchiveFormats.Zip, isWindows: true);

        // Preset none plus explicit keys: only what the config specifies, with trimming
        // still suppressed behind the AOT switch.
        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishAot=true | -p:PublishReadyToRun=true | --self-contained=false");
    }

    [Test]
    public async Task An_explicit_false_overrides_a_preset_that_enabled_the_flag()
    {
        ManifestBuildSection section = new() { PublishAot = false };

        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.AotPlusTrimming, section, ArchiveFormats.Zip, isWindows: true);

        // AOT off re-exposes the preset's trimming default.
        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishTrimmed=true | --self-contained=true | -p:PublishSingleFile=true");
    }

    [Test]
    public async Task The_single_file_opt_out_beats_the_os_default()
    {
        ManifestBuildSection section = new() { SingleFile = false };

        PublishSwitchSet switches = BuildSwitchResolver.Resolve(
            BuildPreset.TrimmingOnly, section, ArchiveFormats.Zip, isWindows: true);

        await Assert.That(Render(switches.ToArguments())).IsEqualTo(
            "-p:PublishTrimmed=true | --self-contained=true | -p:PublishSingleFile=false");
    }

    [Test]
    public async Task An_unknown_preset_fails_naming_the_known_set()
    {
        BuildStageException exception = Capture.Exception<BuildStageException>(
            () => BuildSwitchResolver.Resolve("bogus", new ManifestBuildSection(), ArchiveFormats.Zip));

        await Assert.That(exception.Message).Contains("Unknown build preset 'bogus'");
        await Assert.That(exception.Message).Contains(BuildPreset.AotPlusTrimming);
        await Assert.That(exception.Message).Contains(BuildPreset.None);
    }

    [Test]
    public async Task The_preset_name_set_is_the_shipped_five()
    {
        await Assert.That(string.Join(", ", BuildPreset.All)).IsEqualTo(
            "aot-plus-trimming, ready-to-run, trimming-only, framework-dependent, none");
        await Assert.That(BuildPreset.IsKnown(BuildPreset.TrimmingOnly)).IsTrue();
        await Assert.That(BuildPreset.IsKnown("Trimming-Only")).IsFalse(); // ordinal spelling only.
    }

    [Test]
    public async Task The_preset_flag_overrides_the_manifest_preset_without_writing_the_manifest()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string manifestPath = workspace.WriteManifest(BuildManifest(BuildPreset.FrameworkDependent));
        string before = await File.ReadAllTextAsync(manifestPath);

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(
                new BuildCommand(services), "build", "--preset", BuildPreset.AotPlusTrimming);

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(invoker.Invocations.Count).IsEqualTo(1);

            string published = RecordingProcessInvoker.Render(invoker.Invocations[0]);
            await Assert.That(published).Contains("-p:PublishAot=true");
            await Assert.That(published.Contains("RollForward", StringComparison.Ordinal)).IsFalse();

            // The flag never writes to the file, so the no-per-key-override rule stands.
            await Assert.That(await File.ReadAllTextAsync(manifestPath)).IsEqualTo(before);
        });
    }

    [Test]
    public async Task The_manifest_preset_applies_when_no_flag_is_passed()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(BuildManifest(BuildPreset.FrameworkDependent));
        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(new BuildCommand(services), "build");

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(invoker.Invocations.Count).IsEqualTo(1);
            await Assert.That(RecordingProcessInvoker.Render(invoker.Invocations[0]))
                .Contains("-p:RollForward=Minor");
        });
    }

    [Test]
    public async Task An_unknown_flag_preset_fails_before_any_publish()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(BuildManifest(BuildPreset.FrameworkDependent));
        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(services), "build", "--preset", "bogus");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            await Assert.That(console).Contains("Unknown preset 'bogus'");
            await Assert.That(invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task An_unknown_manifest_preset_fails_naming_it()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(BuildManifest("turbo"));
        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(new BuildCommand(services), "build");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            await Assert.That(console).Contains("Unknown preset 'turbo'");
            await Assert.That(invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    private static string Render(IReadOnlyList<string> arguments) => string.Join(" | ", arguments);

    /// <summary>
    /// A package-kind manifest (never archived, so the fake publish is the whole stage product)
    /// with the given preset seeded into the build section.
    /// </summary>
    /// <param name="preset">The preset name to seed.</param>
    private static string BuildManifest(string preset) =>
        $$"""
        {
          "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ],
          "build": { "preset": "{{preset}}", "runtimeIdentifiers": ["linux-x64"] },
          "publish": { "sources": [] }
        }
        """;
}
