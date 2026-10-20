namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The manifest contract: strict System.Text.Json parsing where an unknown field,
/// an inline comment, or a trailing comma fails with an error naming the offender, plus the
/// validation rules that keep targets, artifact kinds, archives, and the executables-kind
/// cross-check honest. The stages' behavior claims rest on this contract.
/// </summary>
public class ManifestParsingTests
{
    [Test]
    public async Task Unknown_root_field_fails_parse_naming_the_field()
    {
        ManifestLoader loader = new();

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Parse("""{ "targets": [], "apiKey": "secret-value" }"""));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
        await Assert.That(exception.Message).Contains("apiKey");
    }

    [Test]
    public async Task Unknown_nested_field_fails_parse_naming_the_field()
    {
        ManifestLoader loader = new();

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Parse(
                """{ "targets": [], "publish": { "sources": [], "apiKey": "secret-value" } }"""));

        // No key-bearing manifest field can ever load - strict parse rejects it first.
        await Assert.That(exception.Message).Contains("apiKey");
    }

    [Test]
    public async Task Inline_comment_is_rejected()
    {
        ManifestLoader loader = new();

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Parse(
                """
                {
                  // comments are not part of the format
                  "targets": []
                }
                """));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
    }

    [Test]
    public async Task Trailing_comma_is_rejected()
    {
        ManifestLoader loader = new();

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Parse("""{ "targets": [], }"""));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
    }

    [Test]
    public async Task Valid_manifest_maps_every_stage_section()
    {
        ManifestLoader loader = new();

        Manifest manifest = loader.Parse(
            """
            {
              "targets": [
                { "project": "src/App/App.csproj", "kinds": ["executables"], "archive": "tar.gz" }
              ],
              "build": {
                "preset": "ready-to-run",
                "runtimeIdentifiers": ["linux-x64", "win-x64"],
                "publishAot": false,
                "singleFile": false
              },
              "test": { "projects": ["tests/App.Tests/App.Tests.csproj"] },
              "publish": { "sources": ["https://example.test/v3/index.json"] }
            }
            """);

        await Assert.That(manifest.Targets.Count).IsEqualTo(1);
        await Assert.That(manifest.Targets[0].Project).IsEqualTo("src/App/App.csproj");
        await Assert.That(manifest.Targets[0].Kinds[0]).IsEqualTo(ArtifactKinds.Executables);
        await Assert.That(manifest.Targets[0].Archive).IsEqualTo(ArchiveFormats.TarGz);
        await Assert.That(manifest.Build.Preset).IsEqualTo(BuildPreset.ReadyToRun);
        await Assert.That(string.Join(", ", manifest.Build.RuntimeIdentifiers))
            .IsEqualTo("linux-x64, win-x64");
        await Assert.That(manifest.Build.PublishAot).IsFalse();
        await Assert.That(manifest.Build.SingleFile).IsFalse();
        await Assert.That(manifest.Test!.Projects[0]).IsEqualTo("tests/App.Tests/App.Tests.csproj");
        await Assert.That(manifest.Publish.Sources[0]).IsEqualTo("https://example.test/v3/index.json");
    }

    [Test]
    public async Task Minimal_manifest_parses_with_documented_defaults()
    {
        ManifestLoader loader = new();

        Manifest manifest = loader.Parse("""{ "targets": [] }""");

        await Assert.That(manifest.Build.Preset).IsNull();
        await Assert.That(manifest.Build.RuntimeIdentifiers).Count().IsEqualTo(0);
        await Assert.That(manifest.Test).IsNull(); // An absent test section warns, never fails.
        await Assert.That(manifest.Publish.Sources).Count().IsEqualTo(0);
    }

    [Test]
    public async Task Target_with_an_unknown_artifact_kind_fails_validation_naming_it()
    {
        ManifestLoader loader = new();
        Manifest manifest = loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["deb"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("'deb'");
        await Assert.That(exception.Message).Contains(ArtifactKinds.Executables);
    }

    [Test]
    public async Task Target_with_no_artifact_kinds_fails_validation()
    {
        ManifestLoader loader = new();
        Manifest manifest = loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": [] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("declares no artifact kinds");
        await Assert.That(exception.Message).Contains("src/App/App.csproj");
    }

    [Test]
    public async Task Target_with_an_empty_project_path_fails_validation()
    {
        ManifestLoader loader = new();
        Manifest manifest = loader.Parse(
            """{ "targets": [ { "project": "", "kinds": ["executables"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("empty 'project' key");
    }

    [Test]
    public async Task Unknown_archive_format_fails_validation_naming_the_known_set()
    {
        ManifestLoader loader = new();
        Manifest manifest = loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archive": "rar" } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("'rar'");
        await Assert.That(exception.Message).Contains("zip, tar.gz, none"); // Error style.
    }

    [Test]
    public async Task Executable_kind_disagreeing_with_the_evaluated_output_type_fails_validation()
    {
        StubProjectInfoProvider provider = new("Library");
        ManifestLoader loader = new(provider);
        Manifest manifest = loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("src/App/App.csproj");
        await Assert.That(exception.Message).Contains("OutputType 'Library'");
        await Assert.That(provider.OutputTypeReads).IsEqualTo(1);
    }

    [Test]
    public async Task Executable_kind_agreeing_with_the_evaluated_output_type_validates()
    {
        StubProjectInfoProvider provider = new("Exe");
        ManifestLoader loader = new(provider);
        Manifest manifest = loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"] } ] }""");

        loader.Validate(manifest, ManifestFileAtTemp());

        await Assert.That(provider.OutputTypeReads).IsEqualTo(1);
    }

    [Test]
    public async Task Package_kind_target_never_consults_the_project_evaluation()
    {
        StubProjectInfoProvider provider = new("Library");
        ManifestLoader loader = new(provider);
        Manifest manifest = loader.Parse(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ] }""");

        loader.Validate(manifest, ManifestFileAtTemp());

        // The cross-check keys on the declared executables kind only.
        await Assert.That(provider.OutputTypeReads).IsEqualTo(0);
    }

    private static FileInfo ManifestFileAtTemp() =>
        new(Path.Combine(Path.GetTempPath(), "releasegen.json"));

    /// <summary>
    /// A fixed OutputType source standing in for the Microsoft.Build evaluation path, counting
    /// how often the OutputType cross-check reads it.
    /// </summary>
    /// <param name="outputType">The OutputType the stub reports.</param>
    private sealed class StubProjectInfoProvider(string outputType) : IProjectInfoProvider
    {
        public int OutputTypeReads { get; private set; }

        public string GetOutputType(FileInfo projectFile)
        {
            OutputTypeReads++;
            return outputType;
        }

        public ProjectInfo DetectProjectInfo(FileInfo projectFile) =>
            throw new NotSupportedException("The manifest contract tests only read OutputType.");

        public bool TryDetectProjectInfo(FileInfo projectFile, out ProjectInfo? projectInfo)
        {
            projectInfo = null;
            return false;
        }
    }
}
