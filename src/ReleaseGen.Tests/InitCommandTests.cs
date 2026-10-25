using ReleaseGen.Cli.Commands;

namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The init stage: the skeleton it writes is minimal, valid, and parses under the
/// same strict contract every other stage reads; an existing manifest is refused unless
/// <c>--overwrite</c> is passed; <c>--preset</c> seeds the build section's preset key and only a
/// known preset may reach the file; and the default path seeds what repo detection classifies
/// with certainty, warning once per uncertain classification and falling back to the minimal
/// shape when detection fails. Both the command and the verification parse come from the
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
    /// known preset may reach the file; and the default path seeds what repo detection classifies
    /// with certainty, warning once per uncertain classification and falling back to the minimal
    /// shape when detection fails. Both the command and the verification parse come from the
    /// same injected scope, so the skeleton is checked against the very loader the stages use.
    /// </summary>
    public InitCommandTests(IManifestLoader loader)
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
            await Assert.That(console).Contains(BuildPreset.Aot);
            await Assert.That(File.Exists(Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)))
                .IsFalse();
        });
    }

    [Test]
    public async Task Detection_seeds_executables_test_projects_and_package_kinds()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        WriteProject(workspace, Path.Combine("src", "App", "App.csproj"));
        WriteProject(workspace, Path.Combine("src", "App.Tests", "App.Tests.csproj"));
        WriteProject(workspace, Path.Combine("src", "Lib", "Lib.csproj"));

        StubProjectInfoProvider provider = new(new Dictionary<string, ProjectEvaluation>
        {
            // An executable that is also a dotnet tool: both kinds, plus the explicit archive
            // list that makes the rid-class default visible in the file.
            ["App.csproj"] = new("Exe", "true", string.Empty),

            // The naming convention wins over the executable OutputType, and the project is
            // not packable, so it seeds as a test project and never as a target.
            ["App.Tests.csproj"] = new("Exe", string.Empty, "false"),

            // A plain packable library: lib-package, and never archived.
            ["Lib.csproj"] = new("Library", string.Empty, "true"),
        });

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(new InitCommand(provider), "init");
                await Assert.That(exitCode).IsEqualTo(0);
            });

            Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(
                Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

            await Assert.That(manifest.Targets).Count().IsEqualTo(2);

            ManifestTarget executable = manifest.Targets
                .Single(target => target.Project == "src/App/App.csproj");
            await Assert.That(executable.Kinds)
                .IsEquivalentTo([ArtifactKinds.Executables, ArtifactKinds.ToolPackage]);
            await Assert.That(executable.ArchiveFormats)
                .IsEquivalentTo([ArchiveFormats.Zip, ArchiveFormats.TarGz]);

            ManifestTarget library = manifest.Targets
                .Single(target => target.Project == "src/Lib/Lib.csproj");
            await Assert.That(library.Kinds).IsEquivalentTo([ArtifactKinds.LibPackage]);
            await Assert.That(library.ArchiveFormats).Count().IsEqualTo(0);

            await Assert.That(manifest.Test).IsNotNull();
            await Assert.That(manifest.Test!.Projects)
                .IsEquivalentTo(["src/App.Tests/App.Tests.csproj"]);

            // Every classification here was certain, so detection warned about nothing.
            await Assert.That(console.Contains("warning:", StringComparison.Ordinal)).IsFalse();
        });
    }

    [Test]
    public async Task An_unclassified_project_warns_once_and_seeds_nothing_for_it()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        WriteProject(workspace, Path.Combine("src", "Weird", "Weird.csproj"));

        StubProjectInfoProvider provider = new(new Dictionary<string, ProjectEvaluation>
        {
            ["Weird.csproj"] = new("Library", string.Empty, "false"),
        });

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(new InitCommand(provider), "init");
                await Assert.That(exitCode).IsEqualTo(0);
            });

            Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(
                Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

            // The uncertain classification seeds nothing, and warns exactly once.
            await Assert.That(manifest.Targets).Count().IsEqualTo(0);
            await Assert.That(manifest.Test).IsNull();
            await Assert.That(console).Contains("warning:");
            await Assert.That(console).Contains("Weird.csproj");
            await Assert.That(console).Contains("seeded nothing");
            await Assert.That(CountOccurrences(console, "Weird.csproj")).IsEqualTo(1);
        });
    }

    [Test]
    public async Task A_test_project_declaring_package_metadata_is_ambiguous_and_seeds_nothing()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        WriteProject(workspace, Path.Combine("src", "App.Tests", "App.Tests.csproj"));

        StubProjectInfoProvider provider = new(new Dictionary<string, ProjectEvaluation>
        {
            ["App.Tests.csproj"] = new("Library", string.Empty, "true"),
        });

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(new InitCommand(provider), "init");
                await Assert.That(exitCode).IsEqualTo(0);
            });

            Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(
                Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

            await Assert.That(manifest.Targets).Count().IsEqualTo(0);
            await Assert.That(manifest.Test).IsNull();
            await Assert.That(console).Contains("ambiguous");
            await Assert.That(CountOccurrences(console, "warning:")).IsEqualTo(1);
        });
    }

    [Test]
    public async Task A_project_that_cannot_be_evaluated_warns_and_falls_back_to_the_minimal_skeleton()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        WriteProject(workspace, Path.Combine("src", "Broken", "Broken.csproj"));

        // Nothing configured: the stub refuses this file the way a broken toolchain would,
        // so detection seeds nothing and init writes today's minimal shape.
        StubProjectInfoProvider provider = new(new Dictionary<string, ProjectEvaluation>());

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(new InitCommand(provider), "init");
                await Assert.That(exitCode).IsEqualTo(0);
            });

            Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(
                Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

            await Assert.That(manifest.Targets).Count().IsEqualTo(0);
            await Assert.That(manifest.Build.RuntimeIdentifiers).Count().IsEqualTo(0);
            await Assert.That(manifest.Build.Preset).IsNull();
            await Assert.That(manifest.Test).IsNull();
            await Assert.That(manifest.Publish.Sources).Count().IsEqualTo(0);
            await Assert.That(console).Contains("could not evaluate");
            await Assert.That(console).Contains("seeded nothing");
        });
    }

    [Test]
    public async Task A_successful_write_prints_the_next_steps_aftercare_note()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(new InitCommand(), "init");
                await Assert.That(exitCode).IsEqualTo(0);
            });

            // Where the seeds live, the next step, and the single-file and archive notes.
            await Assert.That(console).Contains("'targets' array");
            await Assert.That(console).Contains("releasegen run");
            await Assert.That(console).Contains("--dry-run");
            await Assert.That(console).Contains("single-file");
            await Assert.That(console).Contains("archiveFormats");
        });
    }

    /// <summary>
    /// Writes a placeholder SDK-style project file under the workspace; classification reads
    /// the injected evaluation, never the file's text.
    /// </summary>
    /// <param name="workspace">The workspace to write into.</param>
    /// <param name="relativePath">The project's path below the workspace root.</param>
    /// <returns>The written project file's full path.</returns>
    private static string WriteProject(TestWorkspace workspace, string relativePath) =>
        workspace.WriteFile(
            relativePath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;
}
