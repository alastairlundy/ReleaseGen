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
using ReleaseGenLib.Exceptions;

/// <summary>
/// The init stage: writes a valid <c>releasegen.json</c> skeleton to the current directory
/// following the per-stage manifest layout. The default path stays non-interactive: it scans
/// the repository for .NET projects and seeds what is certain - executable targets, test
/// projects by naming convention, and package kinds from project metadata - warning once per
/// uncertain classification and seeding nothing for it, falling back to the minimal skeleton
/// when detection fails or turns ambiguous. <c>--preset</c> seeds the build section's
/// <c>preset</c> key; without the flag, or with <c>none</c>, the key stays out of the skeleton
/// (absence means preset none). <c>--interactive</c> opts into the five-question questionnaire
/// that writes the tailored shape instead, refused outright where stdin is not a console. Both
/// write paths parse and validate the composed manifest through the same strict loader the
/// stages use before it reaches disk, so a validation failure writes nothing and exits 1. An
/// existing manifest is refused with exit code 1 unless <c>--overwrite</c> is passed. Init is
/// a one-shot command and takes no part in the chained run.
/// </summary>
public sealed class InitCommand : Command
{
    private readonly RepoDetector _repoDetector;
    private readonly ManifestLoader _manifestLoader;
    private readonly InitConsole _interactiveConsole;
    private readonly Func<bool> _sdkSupportsOsxX64;

    /// <summary>
    /// The init stage: writes a valid <c>releasegen.json</c> manifest to the current directory,
    /// seeded from repo detection where detection succeeds, or shaped by the
    /// <c>--interactive</c> questionnaire where that flag opts in.
    /// </summary>
    /// <param name="projectInfoProvider">
    /// The Microsoft.Build evaluation path repo detection reads; the same provider answers the
    /// pre-write verification's kind cross-checks. Tests inject a fixed evaluation instead of
    /// touching the real toolchain.
    /// </param>
    /// <param name="interactiveConsole">
    /// The channel the questionnaire reads answers from and writes prompts to; the process
    /// console when omitted. Tests inject a scripted reader instead of touching stdin.
    /// </param>
    /// <param name="sdkSupportsOsxX64">
    /// The installed-SDK clause of the rid default union rule; the real RID-catalog probe when
    /// omitted. Tests inject an answer to pin the machine out of the default rid set.
    /// </param>
    public InitCommand(
        IProjectInfoProvider? projectInfoProvider = null,
        InitConsole? interactiveConsole = null,
        Func<bool>? sdkSupportsOsxX64 = null)
        : base("init", "Writes a minimal valid releasegen.json manifest.")
    {
        IProjectInfoProvider evaluationProvider = projectInfoProvider ?? new ProjectInfoProvider();

        _repoDetector = new RepoDetector(evaluationProvider);

        // The same evaluation path powers the pre-write verification: the composed manifest is
        // checked against the very loader (and provider) the stages run.
        _manifestLoader = new ManifestLoader(evaluationProvider);

        _interactiveConsole = interactiveConsole ??
            new InitConsole(Console.In, Console.Out, !Console.IsInputRedirected);
        _sdkSupportsOsxX64 = sdkSupportsOsxX64 ?? RidDefaults.InstalledSdkSupportsOsxX64;

        Option<string?> presetOption = new("--preset")
        {
            Description = "Seed the manifest's build.preset key with a known preset name. Use 'none' or omit the flag for no preset.",
        };

        Option<bool> overwriteOption = new("--overwrite")
        {
            Description = "Replace releasegen.json when it already exists in the current directory.",
        };

        Option<bool> interactiveOption = new("--interactive")
        {
            Description = "Ask five questions (preset, targets, runtime identifiers, archive formats, test projects) and write the tailored manifest. Needs a console on stdin.",
        };

        Add(presetOption);
        Add(overwriteOption);
        Add(interactiveOption);

        SetAction(async (parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - a refused preset, a failed
            // scan, an unwritable manifest - is caught here and rendered as the one-line error.
            try
            {
                // Only a known preset may reach the skeleton; 'none' — like absence — seeds no key.
                (bool rejected, string? seededPreset) =
                    ResolveSeededPreset(parseResult.GetValue(presetOption));

                if (rejected)
                {
                    return 1;
                }

                bool interactive = parseResult.GetValue(interactiveOption);

                // Piped or redirected stdin cannot answer prompts, so the questionnaire refuses
                // before anything is scanned or written and names the flag it came from.
                if (interactive && !_interactiveConsole.InputIsConsole)
                {
                    CliOutput.ErrorWithoutHint(
                        "--interactive needs an interactive console: stdin is not a terminal, so " +
                        "nobody can answer the five prompts. Run plain 'releasegen init' to write " +
                        "the detected skeleton instead.");
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

                // One warning per uncertain classification, printed before the write (and before
                // the prompts) so the defaults that follow can say what detection left out.
                foreach (string warning in detection.Warnings)
                {
                    CliOutput.Warning(warning);
                }

                string? preset;
                IReadOnlyList<ManifestTarget> targets;
                IReadOnlyList<string> runtimeIdentifiers;
                IReadOnlyList<string> testProjects;

                if (interactive)
                {
                    // The questionnaire owns every key it asks about; input ending mid-question
                    // returns null with the error already printed, so nothing is written.
                    InitAnswers? answers = InitInteractive.Run(
                        detection, seededPreset, _interactiveConsole, _sdkSupportsOsxX64);

                    if (answers is null)
                    {
                        return 1;
                    }

                    preset = answers.Preset;
                    targets = answers.Targets;
                    runtimeIdentifiers = answers.RuntimeIdentifiers;
                    testProjects = answers.TestProjects;
                }
                else
                {
                    preset = seededPreset;
                    targets = detection.Targets;
                    runtimeIdentifiers = [];
                    testProjects = detection.TestProjects;
                }

                string manifestContents = ComposeSkeleton(preset, targets, runtimeIdentifiers, testProjects);

                // Belt and braces before the write: the composed content must clear the same
                // strict parse and validation the stages run, so init can never write a manifest
                // the next run would refuse - the questionnaire's guards answer to the same sets,
                // but neither they nor detection are trusted on their own.
                try
                {
                    Manifest composed = _manifestLoader.Parse(manifestContents);
                    _manifestLoader.Validate(composed, manifestFile);
                }
                catch (ManifestException ex)
                {
                    CliOutput.ErrorWithoutHint(
                        $"init: the composed manifest failed validation, so nothing was written. {ex.Message}");
                    return 1;
                }

                await File.WriteAllTextAsync(manifestFile.FullName, manifestContents)
                    .ConfigureAwait(true);

                CliOutput.Note(preset is null
                    ? $"init: wrote {manifestFile.FullName} (no build preset)."
                    : $"init: wrote {manifestFile.FullName} (build preset '{preset}').");

                WriteAftercare(manifestFile, root, targets.Count, testProjects.Count);

                return 0;
            }
            catch (Exception ex)
            {
                // Init registers no --verbose option, so its errors name their own next step
                // instead of the hint line.
                CliOutput.ErrorWithoutHint(ex.Message);
                return 1;
            }
        });
    }

    /// <summary>
    /// Resolves <c>--preset</c> to the preset the skeleton seeds: a blank flag or the
    /// <c>none</c> preset seeds no key, and an unknown name is rejected here with init's own
    /// next step (the command registers no <c>--verbose</c> hint), reported through the return
    /// value so the caller can exit 1 without writing - or, inside the questionnaire, ask the
    /// question again. Shared by both paths so a typed answer and a flag answer are judged
    /// against exactly one known set.
    /// </summary>
    /// <param name="preset">The raw flag value.</param>
    /// <returns>Whether the name was rejected, and the preset to seed when it was not.</returns>
    internal static (bool Rejected, string? SeededPreset) ResolveSeededPreset(string? preset)
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
    /// The post-write aftercare note: where the written content lives, the next step
    /// (<c>releasegen run</c>, with the dry-run rehearsal beside it), and the two notes that
    /// keep first-run surprises down - the single-file rules and the per-rid archive defaults.
    /// Both write paths pass their own counts, so the note describes what this run actually
    /// wrote - detected seeds or questionnaire answers.
    /// </summary>
    /// <param name="manifestFile">The manifest just written.</param>
    /// <param name="root">The directory init scanned.</param>
    /// <param name="targetCount">How many targets the write put in the manifest.</param>
    /// <param name="testProjectCount">How many test projects the write put in the manifest.</param>
    private static void WriteAftercare(
        FileInfo manifestFile, DirectoryInfo root, int targetCount, int testProjectCount)
    {
        string manifestName = Path.GetFileName(manifestFile.FullName);

        CliOutput.Note(targetCount > 0 || testProjectCount > 0
            ? $"init: seeded {targetCount} target(s) and {testProjectCount} test project(s) from the projects under '{root.FullName}'; they live in the 'targets' array and the 'test.projects' key of {manifestName}."
            : $"init: nothing was seeded from the projects under '{root.FullName}'; the 'targets' array in {manifestName} starts empty, so add the projects you want to release.");

        CliOutput.Note(
            $"init: next: review {manifestName}, then run 'releasegen run' - add '--dry-run' to rehearse the same plan without writing anything.");

        CliOutput.Note(
            "init: single-file: the OS defaults stay in force (Unix-based OSes always, Windows only where the target's archive format is zip); 'build.singleFile': false opts out.");

        CliOutput.Note(
            "init: archive: an absent 'archiveFormats' key resolves per rid class - zip for Windows-class and any-* rids, zip plus tar.gz for Unix-based rids - and 'tar.xz' is opt-in. Seeded executable targets carry that default set as an explicit list.");
    }

    /// <summary>
    /// Composes the init manifest: the per-stage layout carrying the chosen targets, a build
    /// section holding the chosen rids, the test section when test projects were chosen (an
    /// absent section keeps the gate's warn-and-pass default, an empty one would run tests
    /// from the current directory), and a publish section with empty sources. When nothing was
    /// chosen the shape is exactly the minimal skeleton. <c>build.preset</c> is written only
    /// when a preset was chosen, and every key matches a JsonPropertyName on the manifest
    /// model, so the emitted file parses strictly through <see cref="ManifestLoader.Parse"/>.
    /// </summary>
    /// <param name="preset">The preset name to seed, or null when none.</param>
    /// <param name="targets">The targets to write: detected seeds on the default path, the questionnaire's choice under <c>--interactive</c>.</param>
    /// <param name="runtimeIdentifiers">
    /// The rid list for the build section: empty on the default path (the minimal skeleton
    /// ships an empty list), the questionnaire's union-rule answer under <c>--interactive</c>.
    /// </param>
    /// <param name="testProjects">The test projects for the test section; none writes no section.</param>
    /// <returns>The complete file contents: pretty-printed manifest JSON indented with two spaces.</returns>
    private static string ComposeSkeleton(
        string? preset,
        IReadOnlyList<ManifestTarget> targets,
        IReadOnlyList<string> runtimeIdentifiers,
        IReadOnlyList<string> testProjects)
    {
        StringBuilder json = new();
        json.AppendLine("{");
        json.AppendLine("  \"targets\": [");

        for (int index = 0; index < targets.Count; index++)
        {
            ManifestTarget target = targets[index];

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

            json.Append(index == targets.Count - 1 ? " }" : " },").AppendLine();
        }

        json.AppendLine("  ],");
        json.AppendLine("  \"build\": {");

        if (preset is not null)
        {
            json.Append("    \"preset\": ").Append(Json(preset)).AppendLine(",");
        }

        json.Append("    \"runtimeIdentifiers\": [")
            .Append(string.Join(", ", runtimeIdentifiers.Select(Json)))
            .AppendLine("]");
        json.AppendLine("  },");

        if (testProjects.Count > 0)
        {
            json.AppendLine("  \"test\": {");
            json.Append("    \"projects\": [")
                .Append(string.Join(", ", testProjects.Select(Json))).AppendLine("]");
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
