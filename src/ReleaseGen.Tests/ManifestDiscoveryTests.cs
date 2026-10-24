using ReleaseGenLib.Exceptions;

namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The manifest location contract: walk-up discovery from a subdirectory like
/// <c>global.json</c> lookup, the explicit-path override that wins when given, and the not-found
/// error that names every searched directory. The manifest file name is <c>releasegen.json</c>.
/// </summary>
[ReleaseGenDi]
public class ManifestDiscoveryTests(IManifestLoader loader)
{
    [Test]
    public async Task Walk_up_discovery_returns_the_first_manifest_on_the_way_up()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string nestedDirectory = workspace.CreateDirectory("src", "Feature", "Deep");
        string nestedManifest = workspace.WriteFile(
            Path.Combine("src", "Feature", ManifestLoader.ManifestFileName),
            ManifestWithPreset(BuildPreset.ReadyToRun));
        workspace.WriteManifest(ManifestWithPreset(BuildPreset.FrameworkDependent));

        FileInfo found = loader.FindManifestFile(new DirectoryInfo(nestedDirectory));

        // Discovery stops at the first manifest walking upward: the nearest one wins.
        await Assert.That(found.FullName).IsEqualTo(Path.GetFullPath(nestedManifest));
    }

    [Test]
    public async Task Discovery_fails_naming_the_searched_directories_when_no_manifest_is_found()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string startDirectory = workspace.CreateDirectory("no", "manifest", "here");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.FindManifestFile(new DirectoryInfo(startDirectory)));

        await Assert.That(exception.Message)
            .Contains($"No {ManifestLoader.ManifestFileName} found");
        await Assert.That(exception.Message).Contains(startDirectory);
    }

    [Test]
    public async Task An_explicit_manifest_path_wins_over_discovery()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string explicitManifest = workspace.WriteManifest(
            ManifestWithPreset(BuildPreset.FrameworkDependent));
        string subdirectory = workspace.CreateDirectory("sub");
        workspace.WriteFile(
            Path.Combine("sub", ManifestLoader.ManifestFileName),
            ManifestWithPreset(BuildPreset.ReadyToRun));

        await TestWorkspace.InDirectoryAsync(subdirectory, async () =>
        {
            Manifest fromExplicitPath = loader.Load(new FileInfo(explicitManifest));
            Manifest fromDiscovery = loader.Load(null);

            await Assert.That(fromExplicitPath.Build.Preset)
                .IsEqualTo(BuildPreset.FrameworkDependent);
            await Assert.That(fromDiscovery.Build.Preset).IsEqualTo(BuildPreset.ReadyToRun);
        });
    }

    [Test]
    public async Task Loading_a_missing_explicit_file_fails_naming_the_path()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string missing = Path.Combine(workspace.Root, "absent", ManifestLoader.ManifestFileName);

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.LoadFromFile(new FileInfo(missing)));

        await Assert.That(exception.Message).Contains("Manifest file not found");
        await Assert.That(exception.Message).Contains(missing);
    }

    private static string ManifestWithPreset(string preset) =>
        $$"""
        {
          "targets": [],
          "build": { "preset": "{{preset}}", "runtimeIdentifiers": [] },
          "publish": { "sources": [] }
        }
        """;
}
