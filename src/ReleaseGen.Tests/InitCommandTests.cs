namespace ReleaseGen.Tests;

using ReleaseGenCli.Commands;
using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The init stage: the skeleton it writes is minimal, valid, and parses under the
/// same strict contract every other stage reads; an existing manifest is refused unless
/// <c>--overwrite</c> is passed; <c>--preset</c> seeds the build section's preset key and only a
/// known preset may reach the file. Both the command and the verification parse come from the
/// same injected scope, so the skeleton is checked against the very loader the stages use.
/// </summary>
[ReleaseGenDi]
public class InitCommandTests
{
    private readonly IManifestLoader _loader;

    /// <summary>
    /// The init stage: the skeleton it writes is minimal, valid, and parses under the
    /// same strict contract every other stage reads; an existing manifest is refused unless
    /// <c>--overwrite</c> is passed; <c>--preset</c> seeds the build section's preset key and only a
    /// known preset may reach the file. Both the command and the verification parse come from the
    /// same injected scope, so the skeleton is checked against the very loader the stages use.
    /// </summary>
    public InitCommandTests(IServiceProvider services, IManifestLoader loader)
    {
        _loader = loader;
    }

    [Test]
    public async Task Init_writes_a_minimal_skeleton_that_parses_strictly()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(new InitCommand(), "init");

            await Assert.That(exitCode).IsEqualTo(0);

            string manifestPath = Path.Combine(workspace.Root, ManifestLoader.ManifestFileName);
            await Assert.That(File.Exists(manifestPath)).IsTrue();

            // The skeleton must load through the same strict parser every stage uses.
            Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(manifestPath));

            await Assert.That(manifest.Targets).Count().IsEqualTo(0);
            await Assert.That(manifest.Build.Preset).IsNull();
            await Assert.That(manifest.Build.RuntimeIdentifiers).Count().IsEqualTo(0);
            await Assert.That(manifest.Test).IsNull();
            await Assert.That(manifest.Publish.Sources).Count().IsEqualTo(0);
        });
    }

    [Test]
    public async Task Init_refuses_to_overwrite_an_existing_manifest_without_the_flag()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string manifestPath = workspace.WriteManifest("sentinel: existing manifest\n");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(new InitCommand(), "init");
                await Assert.That(exitCode).IsEqualTo(1);
            });

            await Assert.That(await File.ReadAllTextAsync(manifestPath)).IsEqualTo("sentinel: existing manifest\n");
            await Assert.That(console).Contains("already exists");
            await Assert.That(console).Contains("--overwrite");
        });
    }

    [Test]
    public async Task Init_replaces_an_existing_manifest_with_overwrite()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string manifestPath = workspace.WriteManifest("not valid json anymore\n");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(
                new InitCommand(), "init", "--overwrite");

            await Assert.That(exitCode).IsEqualTo(0);

            Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(manifestPath));
            await Assert.That(manifest.Targets).Count().IsEqualTo(0);
        });
    }

    [Test]
    public async Task Preset_flag_seeds_the_build_section_preset_key()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(
                new InitCommand(), "init", "--preset", BuildPreset.TrimmingOnly);

            await Assert.That(exitCode).IsEqualTo(0);

            Manifest manifest = _loader.Parse(
                await File.ReadAllTextAsync(Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

            await Assert.That(manifest.Build.Preset).IsEqualTo(BuildPreset.TrimmingOnly);
        });
    }

    [Test]
    public async Task Preset_none_seeds_no_preset_key()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(
                new InitCommand(), "init", "--preset", BuildPreset.None);

            await Assert.That(exitCode).IsEqualTo(0);

            Manifest manifest = _loader.Parse(
                await File.ReadAllTextAsync(Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

            // Absence means preset none; 'none' written as a key would be redundant.
            await Assert.That(manifest.Build.Preset).IsNull();
        });
    }

    [Test]
    public async Task An_unknown_preset_fails_naming_the_known_set_and_writes_nothing()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new InitCommand(), "init", "--preset", "turbo");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            await Assert.That(console).Contains("Unknown preset 'turbo'");
            await Assert.That(console).Contains(BuildPreset.AotPlusTrimming);
            await Assert.That(File.Exists(Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)))
                .IsFalse();
        });
    }
}
