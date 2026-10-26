using ReleaseGenLib.Exceptions;

namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The manifest contract: strict System.Text.Json parsing where an unknown field,
/// an inline comment, or a trailing comma fails with an error naming the offender, plus the
/// validation rules that keep targets, artifact kinds, archives, and the executables-kind
/// cross-check honest. The stages' behavior claims rest on this contract. Parsing and the
/// provider-independent validation rules run through the shared DI-resolved loader; the kind
/// cross-check tests below construct their own loader over a counting stub because
/// the stub's configured evaluation and read count are the subject under test.
/// </summary>
[ReleaseGenDi]
public class ManifestParsingTests
{
    private readonly IManifestLoader _loader;

    /// <summary>
    /// The manifest contract: strict System.Text.Json parsing where an unknown field,
    /// an inline comment, or a trailing comma fails with an error naming the offender, plus the
    /// validation rules that keep targets, artifact kinds, archives, and the executables-kind
    /// cross-check honest. The stages' behavior claims rest on this contract. Parsing and the
    /// provider-independent validation rules run through the shared DI-resolved loader; the kind
    /// cross-check tests below construct their own loader over a counting stub because
    /// the stub's configured evaluation and read count are the subject under test.
    /// </summary>
    public ManifestParsingTests(IManifestLoader loader)
    {
        _loader = loader;
    }

    [Test]
    public async Task Unknown_root_field_fails_parse_naming_the_field()
    {
        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Parse("""{ "targets": [], "apiKey": "secret-value" }"""));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
        await Assert.That(exception.Message).Contains("apiKey");
    }

    [Test]
    public async Task Unknown_nested_field_fails_parse_naming_the_field()
    {
        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Parse(
                """{ "targets": [], "publish": { "sources": [], "apiKey": "secret-value" } }"""));

        // No key-bearing manifest field can ever load - strict parse rejects it first.
        await Assert.That(exception.Message).Contains("apiKey");
    }

    [Test]
    public async Task Inline_comment_is_rejected()
    {
        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Parse(
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
        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Parse("""{ "targets": [], }"""));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
    }

    [Test]
    public async Task Valid_manifest_maps_every_stage_section()
    {
        Manifest manifest = _loader.Parse(
            """
            {
              "targets": [
                { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": "tar.gz" }
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
        await Assert.That(manifest.Targets[0].ArchiveFormats).IsEquivalentTo([ArchiveFormats.TarGz]);
        await Assert.That(manifest.Build.Preset).IsEqualTo(BuildPreset.ReadyToRun);
        await Assert.That(string.Join(", ", manifest.Build.RuntimeIdentifiers))
            .IsEqualTo("linux-x64, win-x64");
        await Assert.That(manifest.Build.PublishAot).IsFalse();
        await Assert.That(manifest.Build.SingleFile).IsFalse();

        await Assert.That(manifest.Test).IsNotNull();
        await Assert.That(manifest.Test.Projects[0]).IsEqualTo("tests/App.Tests/App.Tests.csproj");
        await Assert.That(manifest.Publish.Sources[0]).IsEqualTo("https://example.test/v3/index.json");
    }

    [Test]
    public async Task Minimal_manifest_parses_with_documented_defaults()
    {
        Manifest manifest = _loader.Parse("""{ "targets": [] }""");

        await Assert.That(manifest.Build.Preset).IsNull();
        await Assert.That(manifest.Build.RuntimeIdentifiers).Count().IsEqualTo(0);
        await Assert.That(manifest.Test).IsNull(); // An absent test section warns, never fails.
        await Assert.That(manifest.Publish.Sources).Count().IsEqualTo(0);
    }

    [Test]
    public async Task Target_with_an_unknown_artifact_kind_fails_validation_naming_it()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["deb"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("'deb'");
        await Assert.That(exception.Message).Contains(ArtifactKinds.Executables);
    }

    [Test]
    public async Task Target_with_no_artifact_kinds_fails_validation()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": [] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("declares no artifact kinds");
        await Assert.That(exception.Message).Contains("src/App/App.csproj");
    }

    [Test]
    public async Task Target_with_an_empty_project_path_fails_validation()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "", "kinds": ["executables"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("empty 'project' key");
    }

    [Test]
    public async Task Unknown_archive_format_fails_validation_naming_the_known_set()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": "rar" } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("'rar'");
        await Assert.That(exception.Message).Contains("zip, tar.gz, tar.xz, none"); // Error style.
    }

    [Test]
    public async Task A_duplicated_archive_format_fails_validation_naming_the_target_and_the_format()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": ["zip", "tar.gz", "zip"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, ManifestFileAtTemp()));

        // The fail-loudly contract names the target and the duplicated value rather than
        // silently deduping; the first duplicate is the one reported.
        await Assert.That(exception.Message).Contains("src/App/App.csproj");
        await Assert.That(exception.Message).Contains("'zip' more than once");
        await Assert.That(exception.Message.Contains("'tar.gz' more than once", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Archive_formats_accept_the_bare_string_spelling()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": "tar.gz" } ] }""");

        // Both spellings normalize to one list at parse time.
        await Assert.That(manifest.Targets[0].ArchiveFormats).IsEquivalentTo([ArchiveFormats.TarGz]);
    }

    [Test]
    public async Task Archive_formats_accept_the_list_spelling()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": ["zip", "tar.gz"] } ] }""");

        await Assert.That(manifest.Targets[0].ArchiveFormats)
            .IsEquivalentTo([ArchiveFormats.Zip, ArchiveFormats.TarGz]);
    }

    [Test]
    public async Task A_malformed_archive_formats_token_fails_parse_naming_the_key()
    {
        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Parse(
                """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": 7 } ] }"""));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
        await Assert.That(exception.Message).Contains("archiveFormats");
    }

    [Test]
    public async Task A_non_string_array_entry_fails_parse_naming_the_key()
    {
        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Parse(
                """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": ["zip", 7] } ] }"""));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
        await Assert.That(exception.Message).Contains("archiveFormats");
    }

    [Test]
    public async Task The_retired_archive_key_fails_strict_parse_naming_the_field()
    {
        // Hard rename: the old key is an unknown field now, like any misspelling.
        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Parse(
                """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archive": "zip" } ] }"""));

        await Assert.That(exception.Message).Contains("Manifest parse failed");
        await Assert.That(exception.Message).Contains("archive");
    }

    [Test]
    public async Task An_unknown_format_inside_a_declared_list_fails_validation_naming_it()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": ["zip", "rar"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, ManifestFileAtTemp()));

        // Every list entry validates, not just the first.
        await Assert.That(exception.Message).Contains("'rar'");
        await Assert.That(exception.Message).Contains("zip, tar.gz, tar.xz, none");
    }

    [Test]
    public async Task Tar_xz_as_the_only_format_against_unsupported_rids_fails_naming_the_conflict()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": "tar.xz" } ], "build": { "runtimeIdentifiers": ["win-x64", "any-x64", "linux-x64"] } }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, ManifestFileAtTemp()));

        // Names the target, the conflicting rids (Windows-class and portable), and the fix -
        // the Unix-based rid is not part of the conflict.
        await Assert.That(exception.Message).Contains("src/App/App.csproj");
        await Assert.That(exception.Message).Contains("tar.xz");
        await Assert.That(exception.Message).Contains("win-x64");
        await Assert.That(exception.Message).Contains("any-x64");
        await Assert.That(exception.Message.Contains("linux-x64", StringComparison.Ordinal)).IsFalse();
        await Assert.That(exception.Message).Contains("archiveFormats");
        await Assert.That(exception.Message).Contains("zip");
    }

    [Test]
    public async Task Zip_alongside_tar_xz_validates_against_windows_rids()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": ["zip", "tar.xz"] } ], "build": { "runtimeIdentifiers": ["win-x64"] } }""");

        // A usable format remains for every rid, so validation passes and the build stage
        // skips tar.xz per Windows rid with one warning each.
        _loader.Validate(manifest, ManifestFileAtTemp());
    }

    [Test]
    public async Task Tar_xz_only_against_unix_rids_validates()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"], "archiveFormats": "tar.xz" } ], "build": { "runtimeIdentifiers": ["linux-x64", "osx-arm64"] } }""");

        // tar.xz is producible for every declared rid, so no conflict can arise.
        _loader.Validate(manifest, ManifestFileAtTemp());
    }

    [Test]
    public async Task An_absent_archive_key_never_conflicts_with_windows_rids()
    {
        Manifest manifest = _loader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"] } ], "build": { "runtimeIdentifiers": ["win-x64", "any-x64"] } }""");

        // Absent keys resolve to the rid-class default sets, never to tar.xz.
        _loader.Validate(manifest, ManifestFileAtTemp());

        await Assert.That(manifest.Targets[0].ArchiveFormats.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Executable_kind_disagreeing_with_the_evaluated_output_type_fails_validation()
    {
        StubProjectInfoProvider provider = new("Library");
        ManifestLoader providerLoader = new(provider);
        Manifest manifest = providerLoader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => providerLoader.Validate(manifest, ManifestFileAtTemp()));

        await Assert.That(exception.Message).Contains("src/App/App.csproj");
        await Assert.That(exception.Message).Contains("OutputType 'Library'");
        await Assert.That(provider.EvaluationReads).IsEqualTo(1);
    }

    [Test]
    public async Task Executable_kind_agreeing_with_the_evaluated_output_type_validates()
    {
        StubProjectInfoProvider provider = new("Exe");
        ManifestLoader providerLoader = new(provider);
        Manifest manifest = providerLoader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables"] } ] }""");

        providerLoader.Validate(manifest, ManifestFileAtTemp());

        await Assert.That(provider.EvaluationReads).IsEqualTo(1);
    }

    [Test]
    public async Task Package_kind_target_never_consults_the_project_evaluation()
    {
        StubProjectInfoProvider provider = new("Library");
        ManifestLoader providerLoader = new(provider);
        Manifest manifest = providerLoader.Parse(
            """{ "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["lib-package"] } ] }""");

        providerLoader.Validate(manifest, ManifestFileAtTemp());

        // The cross-check keys on the declared kinds: neither kind is declared, so the
        // evaluation pass never runs.
        await Assert.That(provider.EvaluationReads).IsEqualTo(0);
    }

    [Test]
    public async Task A_target_declaring_both_kinds_is_answered_by_one_evaluation_pass()
    {
        StubProjectInfoProvider provider = new();
        ManifestLoader providerLoader = new(provider);
        Manifest manifest = providerLoader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables", "tool-package"] } ] }""");

        providerLoader.Validate(manifest, ManifestFileAtTemp());

        // The OutputType and PackAsTool cross-checks share one pass over the project file.
        await Assert.That(provider.EvaluationReads).IsEqualTo(1);
    }

    [Test]
    public async Task Tool_package_kind_without_pack_as_tool_fails_validation_naming_the_fix()
    {
        StubProjectInfoProvider provider = new(packAsTool: false);
        ManifestLoader providerLoader = new(provider);
        Manifest manifest = providerLoader.Parse(
            """{ "targets": [ { "project": "src/App/App.csproj", "kinds": ["tool-package"] } ] }""");

        ManifestException exception = Capture.Exception<ManifestException>(
            () => providerLoader.Validate(manifest, ManifestFileAtTemp()));

        // The error names the target, the property, and a declaration site that does not
        // assume the project file: the property commonly lives in an imported props file.
        await Assert.That(exception.Message).Contains("src/App/App.csproj");
        await Assert.That(exception.Message).Contains(ArtifactKinds.ToolPackage);
        await Assert.That(exception.Message).Contains("PackAsTool");
        await Assert.That(exception.Message).Contains("Directory.Build.props");
        await Assert.That(provider.EvaluationReads).IsEqualTo(1);
    }

    private static FileInfo ManifestFileAtTemp() =>
        new(Path.Combine(Path.GetTempPath(), "releasegen.json"));
}
