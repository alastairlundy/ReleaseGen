/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

namespace ReleaseGen.Cli.Commands;

using System.Text;
using System.Text.Json;

/// <summary>
/// The init stage: writes a valid <c>releasegen.json</c> skeleton to the current directory
/// following the per-stage manifest layout. The default path stays non-interactive: it scans
/// the repository for .NET projects and seeds what is certain - executable targets, test
/// projects by naming convention, and package kinds from project metadata - warning once per
/// uncertain classification and seeding nothing for it, falling back to the minimal skeleton
/// when detection fails or turns ambiguous. <c>--preset</c> seeds the build section's
/// <c>preset</c> key; without the flag, or with <c>none</c>, the key stays out of the skeleton
/// (absence means preset none). An existing manifest is refused with exit code 1 unless
/// <c>--overwrite</c> is passed. Init is a one-shot command and takes no part in the chained run.
/// </summary>
public sealed class InitCommand : Command
{
    private readonly RepoDetector _repoDetector;

    /// <summary>
    /// The init stage: writes a valid <c>releasegen.json</c> skeleton to the current directory,
    /// seeded from repo detection where detection succeeds.
    /// </summary>
    /// <param name="projectInfoProvider">
    /// The Microsoft.Build evaluation path repo detection reads; the production provider when
    /// omitted. Tests inject a fixed evaluation instead of touching the real toolchain.
    /// </param>
    public InitCommand(IProjectInfoProvider? projectInfoProvider = null)
        : base("init", "Writes a minimal valid releasegen.json manifest.")
    {
        _repoDetector = new RepoDetector(projectInfoProvider ?? new ProjectInfoProvider());

        Option<string?> presetOption = new("--preset")
        {
            Description = "Seed the manifest's build.preset key with a known preset name. Use 'none' or omit the flag for no preset.",
        };

        Option<bool> overwriteOption = new("--overwrite")
        {
            Description = "Replace releasegen.json when it already exists in the current directory.",
        };

        Add(presetOption);
        Add(overwriteOption);

        SetAction(async (parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // Only a known preset may reach the skeleton; 'none' — like absence — seeds no key.
            (bool rejected, string? seededPreset) =
                ResolveSeededPreset(parseResult.GetValue(presetOption));

            if (rejected)
            {
                return 1;
            }

            FileInfo manifestFile = new(Path.Combine(Environment.CurrentDirectory, ManifestLoader.ManifestFileName));

            // Refusing an expected condition is control flow, not an exception.
            if (RefusesExistingManifest(manifestFile, parseResult.GetValue(overwriteOption)))
            {
                return 1;
            }

            // Detection runs read-only, after the refusal so a refused init still scans nothing.
            DirectoryInfo root = new(Environment.CurrentDirectory);
            RepoDetection detection = _repoDetector.Detect(root);

            // One warning per uncertain classification, printed before the write so the note
            // that follows can say what was seeded instead.
            foreach (string warning in detection.Warnings)
            {
                CliOutput.Warning(warning);
            }

            await File.WriteAllTextAsync(
                manifestFile.FullName, ComposeSkeleton(seededPreset, detection)).ConfigureAwait(true);

            CliOutput.Note(seededPreset is null
                ? $"init: wrote {manifestFile.FullName} (no build preset)."
                : $"init: wrote {manifestFile.FullName} (build preset '{seededPreset}').");

            WriteAftercare(manifestFile, root, detection);

            return 0;
        });
    }

    /// <summary>
    /// Resolves <c>--preset</c> to the preset the skeleton seeds: a blank flag or the
    /// <c>none</c> preset seeds no key, and an unknown name is rejected here with init's own
    /// next step (the command registers no <c>--verbose</c> hint), reported through the return
    /// value so the action can exit 1 without writing.
    /// </summary>
    /// <param name="preset">The raw flag value.</param>
    /// <returns>Whether the name was rejected, and the preset to seed when it was not.</returns>
    private static (bool Rejected, string? SeededPreset) ResolveSeededPreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return (false, null);
        }

        if (!BuildPreset.TryParse(preset, out string canonicalPreset))
        {
            CliOutput.ErrorWithoutHint(
                $"Unknown preset '{preset}'. Known presets: {string.Join(", ", BuildPreset.All)}.");
            return (true, null);
        }

        return (false, string.Equals(canonicalPreset, BuildPreset.None, StringComparison.Ordinal)
            ? null
            : canonicalPreset);
    }

    /// <summary>
    /// Whether the run must stop because a manifest already exists and <c>--overwrite</c> was
    /// not passed: the refusal prints init's own next step (the command registers no
    /// <c>--verbose</c> hint) and exits 1 without scanning or writing.
    /// </summary>
    /// <param name="manifestFile">The manifest path init would write.</param>
    /// <param name="overwrite">The <c>--overwrite</c> flag's value.</param>
    /// <returns><see langword="true"/> when the run is refused; the error is already printed.</returns>
    private static bool RefusesExistingManifest(FileInfo manifestFile, bool overwrite)
    {
        if (manifestFile.Exists && !overwrite)
        {
            CliOutput.ErrorWithoutHint(
                $"{ManifestLoader.ManifestFileName} already exists at '{manifestFile.FullName}'. " +
                "Re-run init with --overwrite to replace it.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// The post-write aftercare note: where the seeded content lives, the next step
    /// (<c>releasegen run</c>, with the dry-run rehearsal beside it), and the two notes that
    /// keep first-run surprises down - the single-file rules and the per-rid archive defaults.
    /// </summary>
    /// <param name="manifestFile">The manifest just written.</param>
    /// <param name="root">The directory init scanned.</param>
    /// <param name="detection">What detection seeded into the skeleton.</param>
    private static void WriteAftercare(FileInfo manifestFile, DirectoryInfo root, RepoDetection detection)
    {
        string manifestName = Path.GetFileName(manifestFile.FullName);

        CliOutput.Note(detection.Targets.Count > 0 || detection.TestProjects.Count > 0
            ? $"init: seeded {detection.Targets.Count} target(s) and {detection.TestProjects.Count} test project(s) from the projects under '{root.FullName}'; they live in the 'targets' array and the 'test.projects' key of {manifestName}."
            : $"init: nothing was seeded from the projects under '{root.FullName}'; the 'targets' array in {manifestName} starts empty, so add the projects you want to release.");

        CliOutput.Note(
            $"init: next: review {manifestName}, then run 'releasegen run' - add '--dry-run' to rehearse the same plan without writing anything.");

        CliOutput.Note(
            "init: single-file: the OS defaults stay in force (Unix-based OSes always, Windows only where the target's archive format is zip); 'build.singleFile': false opts out.");

        CliOutput.Note(
            "init: archive: an absent 'archiveFormats' key resolves per rid class - zip for Windows-class and any-* rids, zip plus tar.gz for Unix-based rids - and 'tar.xz' is opt-in. Seeded executable targets carry that default set as an explicit list.");
    }

    /// <summary>
    /// Composes the init skeleton: the per-stage layout carrying the detected targets, a build
    /// section, the test section when test projects were detected (an absent section keeps the
    /// gate's warn-and-pass default, an empty one would run tests from the current directory),
    /// and a publish section with empty sources. When detection seeded nothing the shape is
    /// exactly the minimal skeleton. <c>build.preset</c> is written only when a preset was
    /// chosen, and every key matches a JsonPropertyName on the manifest model, so the emitted
    /// file parses strictly through <see cref="ManifestLoader.Parse"/>.
    /// </summary>
    /// <param name="preset">The preset name to seed, or null when none.</param>
    /// <param name="detection">What repo detection seeded.</param>
    /// <returns>The complete file contents: pretty-printed manifest JSON indented with two spaces.</returns>
    private static string ComposeSkeleton(string? preset, RepoDetection detection)
    {
        StringBuilder json = new();
        json.AppendLine("{");
        json.AppendLine("  \"targets\": [");

        for (int index = 0; index < detection.Targets.Count; index++)
        {
            ManifestTarget target = detection.Targets[index];

            json.Append("    { ");
            json.Append("\"project\": ").Append(Json(target.Project)).Append(", ");
            json.Append("\"kinds\": [").Append(string.Join(", ", target.Kinds.Select(Json))).Append(']');

            // Only executables-kind targets carry the key: package kinds are never archived,
            // and the explicit list makes the rid-class default visible in the file.
            if (target.ArchiveFormats.Length > 0)
            {
                json.Append(", \"archiveFormats\": [")
                    .Append(string.Join(", ", target.ArchiveFormats.Select(Json))).Append(']');
            }

            json.Append(index == detection.Targets.Count - 1 ? " }" : " },").AppendLine();
        }

        json.AppendLine("  ],");
        json.AppendLine("  \"build\": {");

        if (preset is not null)
        {
            json.Append("    \"preset\": ").Append(Json(preset)).AppendLine(",");
        }

        json.AppendLine("    \"runtimeIdentifiers\": []");
        json.AppendLine("  },");

        if (detection.TestProjects.Count > 0)
        {
            json.AppendLine("  \"test\": {");
            json.Append("    \"projects\": [")
                .Append(string.Join(", ", detection.TestProjects.Select(Json))).AppendLine("]");
            json.AppendLine("  },");
        }

        json.AppendLine("  \"publish\": {");
        json.AppendLine("    \"sources\": []");
        json.AppendLine("  }");
        json.AppendLine("}");

        // Line endings already follow this OS (StringBuilder), and the closing brace line is
        // terminated like every other line.
        return json.ToString();
    }

    /// <summary>
    /// One JSON string literal: project paths and preset names are escaped through the serializer
    /// so a path with a quote or a backslash cannot break the emitted manifest.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The quoted, escaped JSON string.</returns>
    private static string Json(string value) => JsonSerializer.Serialize(value);
}
