/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

namespace ReleaseGen.Cli.Commands;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The build stage: per-rid <c>dotnet publish -c Release -r &lt;rid&gt;</c> through the
/// <see cref="IBuildStageRunner"/>, driven by the manifest's build section. The
/// <c>--preset</c> flag overrides the manifest's <c>build.preset</c> for this invocation only
/// and never writes the file; an unknown preset from either source fails with an error
/// naming it and the known set, exit 1. The <c>--rids</c> flag works the same way over the
/// manifest's <c>runtimeIdentifiers</c>: a comma-separated subset for this invocation only,
/// refused with an error naming the offending value and the manifest's own set when it is
/// empty, carries a blank entry, or names a rid the manifest does not declare. Each publish
/// and archive is a shared planned record: the run mode decides at the runner boundary
/// whether the stage executes it or prints it without writing anything, and <c>--dry-run</c>
/// is that decision's flag - it prints the plan, writes nothing anywhere (including the
/// <c>release/</c> tree), and exits on manifest validation alone. <c>--verbose</c> is the
/// other half of the record's audience: a real run under it shows each spawned command line,
/// its raw output, and the record's cause line, and combining it with <c>--quiet</c> fails
/// with the conflict error before the stage starts. Output stays local under
/// <c>release/&lt;rid&gt;/&lt;project-name&gt;/</c> with per-target archives beside the rid
/// folders - nothing is uploaded.
/// </summary>
/// <remarks>
/// Publish output is routed through the conventions layer: stdout lines become stage
/// notes, MSBuild diagnostic lines (<c>: warning </c>/<c>: error </c>, which is how the SDK's
/// IL3000-family analyzer warnings surface) and all stderr lines become warnings so they stay
/// visible under <c>--quiet</c>. No publish line is dropped.
/// </remarks>
public sealed class BuildCommand : Command
{
    public BuildCommand(IManifestLoader manifestLoader, IBuildStageRunner buildStageRunner) :
        base("build", "Builds release binaries for the manifest's targets.")
    {
        Option<string?> presetOption = new("--preset")
        {
            Description = "Override the manifest's build.preset for this invocation only. Does not write the manifest.",
        };

        Option<string?> ridsOption = new("--rids")
        {
            Description =
                "Comma-separated subset of the manifest's runtimeIdentifiers to build for this invocation only. Refused when a value is blank or not in the manifest's set, and never writes the manifest.",
        };

        Option<bool> dryRunOption = CliOptions.CreateDryRun();
        Option<bool> verboseOption = CliOptions.CreateVerbose();

        Add(presetOption);
        Add(ridsOption);
        Add(dryRunOption);
        Add(verboseOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliOutput.ApplyOutputModes(parseResult, verboseOption))
            {
                return 1;
            }

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - refused preset, refused
            // rid subset, manifest error, failed publish - is caught here and rendered as the
            // one-line error.
            try
            {
                // Preset precedence: --preset beats the manifest key; neither present means preset none.
                string? flagPreset = parseResult.GetValue(presetOption);
                string? effectivePreset = !string.IsNullOrWhiteSpace(flagPreset) ? flagPreset : null;

                // An unknown preset is a refused condition, not an exception.
                if (effectivePreset is not null && !BuildPreset.TryParse(effectivePreset, out _))
                {
                    CliOutput.Error(
                        $"Unknown preset '{effectivePreset}'. Known presets: {string.Join(", ", BuildPreset.All)}.");
                    return 1;
                }

                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                if (effectivePreset is null)
                {
                    // The manifest's own preset still validates: unknown names fail with the set.
                    string? manifestPreset = manifest.Build.Preset;

                    if (!string.IsNullOrWhiteSpace(manifestPreset) && !BuildPreset.TryParse(manifestPreset, out _))
                    {
                        CliOutput.Error(
                            $"Unknown preset '{manifestPreset}'. Known presets: {string.Join(", ", BuildPreset.All)}.");
                        return 1;
                    }

                    effectivePreset = manifestPreset;
                }

                string preset = string.IsNullOrWhiteSpace(effectivePreset) ? BuildPreset.None : effectivePreset;

                // The --rids subset, resolved against the manifest's own set: it may only
                // carve a subset out of what the manifest declares, never add to it. The
                // refusal happens before any planning, mirroring the unknown-preset refusal.
                if (!TryResolveRidSubset(parseResult.GetValue(ridsOption), manifest, out string[]? ridSubset, out string? subsetRefusal))
                {
                    CliOutput.Error(subsetRefusal);
                    return 1;
                }

                // One decision point for the runner: the flag. The stage still plans every
                // publish and archive identically in both modes; only the runner's branch
                // differs.
                StageRunMode runMode = parseResult.GetValue(dryRunOption)
                    ? StageRunMode.DryRun
                    : StageRunMode.Execute;

                BuildStageRequest request = new()
                {
                    Manifest = manifest,
                    ManifestDirectory = manifestFile.Directory ?? new DirectoryInfo(Environment.CurrentDirectory),
                    Preset = preset,
                    RuntimeIdentifierSubset = ridSubset,
                    OutputRoot = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "release")),
                    WriteLine = RouteStdoutLine,
                    WriteErrorLine = static line => CliOutput.Warning(line),
                    WriteDiagnosisLine = CliOutput.Cause,
                    RunMode = runMode,
                };

                await buildStageRunner.RunAsync(request, cancellationToken).ConfigureAwait(true);
                return 0;
            }
            catch (Exception ex)
            {
                CliOutput.Error(ex.Message);
                return 1;
            }
        });
    }

    /// <summary>
    /// Resolves the <c>--rids</c> value against the manifest's configured set: absent means
    /// the full manifest set, a comma-separated list is trimmed, case-insensitively
    /// de-duplicated, and every value must already be in the manifest's
    /// <c>runtimeIdentifiers</c> - the subset can only narrow the manifest's decision. The
    /// manifest's spelling wins so output paths stay deterministic.
    /// </summary>
    /// <param name="flagValue">The raw <c>--rids</c> option value; null means no subset, and an empty or whitespace value is a refusal.</param>
    /// <param name="manifest">The loaded manifest whose build section declares the full set.</param>
    /// <param name="subset">The normalized subset in the manifest's spelling, or null for the full set.</param>
    /// <param name="refusal">The one-line refusal message when the flag is refused; otherwise null. Not null when the method returns false.</param>
    /// <returns>True when <paramref name="subset"/> carries the invocation's rid set; false when the caller must refuse.</returns>
    private static bool TryResolveRidSubset(
        string? flagValue,
        Manifest manifest,
        out string[]? subset,
        [NotNullWhen(false)] out string? refusal)
    {
        if (flagValue is null)
        {
            subset = null;
            refusal = null;
            return true;
        }

        string configuredSet = string.Join(", ", manifest.Build.RuntimeIdentifiers);

        if (string.IsNullOrWhiteSpace(flagValue))
        {
            subset = null;
            refusal =
                $"--rids names no runtime identifier; provide a comma-separated subset of the manifest's runtimeIdentifiers ({configuredSet}).";
            return false;
        }

        // Trim but do not drop: a blank entry is a refusal naming its position, matching the
        // manifest's publish.sources strictness - silently dropping one would alias the
        // caller's mistake onto the full set.
        string[] entries = flagValue.Split(',', StringSplitOptions.TrimEntries);

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Length == 0)
            {
                subset = null;
                refusal =
                    $"--rids entry {i + 1} is blank; provide a comma-separated subset of the manifest's runtimeIdentifiers ({configuredSet}).";
                return false;
            }
        }

        string[] subsetWithFlagSpelling = entries;

        string[] deduplicated = subsetWithFlagSpelling
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string rid in deduplicated)
        {
            if (!manifest.Build.RuntimeIdentifiers
                .Any(manifestRid => string.Equals(manifestRid, rid, StringComparison.OrdinalIgnoreCase)))
            {
                subset = null;
                refusal =
                    $"--rids value '{rid}' is not in the manifest's runtimeIdentifiers ({configuredSet}); the flag can only narrow the manifest's set, never add to it.";
                return false;
            }
        }

        // Replace each admitted entry with the manifest's spelling.
        subset = deduplicated
            .Select(rid => manifest.Build.RuntimeIdentifiers.First(manifestRid =>
                string.Equals(manifestRid, rid, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        refusal = null;
        return true;
    }

    /// <summary>
    /// Routes one captured publish stdout line through the conventions layer: MSBuild
    /// diagnostic lines - the shape IL3000-family analyzer warnings arrive in - become
    /// warnings so they survive <c>--quiet</c>; everything else becomes a stage note.
    /// </summary>
    /// <param name="line">The stdout line exactly as the publish emitted it.</param>
    private static void RouteStdoutLine(string line)
    {
        string trimmed = line.TrimStart();

        bool diagnostic =
            line.Contains(": warning ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(": error ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("warning ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("error ", StringComparison.OrdinalIgnoreCase);

        if (diagnostic)
        {
            CliOutput.Warning(line);
        }
        else
        {
            CliOutput.Note(line);
        }
    }
}
