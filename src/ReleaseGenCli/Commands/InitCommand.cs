namespace ReleaseGenCli.Commands;

using System.CommandLine;

/// <summary>
/// The init stage: writes a minimal valid <c>releasegen.json</c> skeleton to the current
/// directory following the per-stage manifest layout. <c>--preset</c> seeds the
/// build section's <c>preset</c> key; without the flag, or with <c>none</c>, the key
/// stays out of the skeleton (absence means preset none). An existing manifest is refused with
/// exit code 1 unless <c>--overwrite</c> is passed. Init is a one-shot command and takes
/// no part in the chained run.
/// </summary>
public sealed class InitCommand : Command
{
    public InitCommand(IServiceProvider services) : base("init", "Writes a minimal valid releasegen.json manifest.")
    {
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

        SetAction((parseResult, _) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // Only a known preset may reach the skeleton; 'none' — like absence — seeds no key.
            string? seededPreset = null;
            string? preset = parseResult.GetValue(presetOption);

            if (!string.IsNullOrWhiteSpace(preset))
            {
                if (!BuildPreset.TryParse(preset, out string canonicalPreset))
                {
                    CliOutput.Error(
                        $"Unknown preset '{preset}'. Known presets: {string.Join(", ", BuildPreset.All)}.");
                    return Task.FromResult(1);
                }

                if (!string.Equals(canonicalPreset, BuildPreset.None, StringComparison.Ordinal))
                {
                    seededPreset = canonicalPreset;
                }
            }

            FileInfo manifestFile = new(Path.Combine(Environment.CurrentDirectory, ManifestLoader.ManifestFileName));

            // Refusing an expected condition is control flow, not an exception.
            if (manifestFile.Exists && !parseResult.GetValue(overwriteOption))
            {
                CliOutput.Error(
                    $"{ManifestLoader.ManifestFileName} already exists at '{manifestFile.FullName}'. " +
                    "Re-run init with --overwrite to replace it.");
                return Task.FromResult(1);
            }

            File.WriteAllText(manifestFile.FullName, ComposeSkeleton(seededPreset));

            CliOutput.Note(seededPreset is null
                ? $"init: wrote {manifestFile.FullName} (no build preset)."
                : $"init: wrote {manifestFile.FullName} (build preset '{seededPreset}').");

            return Task.FromResult(0);
        });
    }

    /// <summary>
    /// Composes the init skeleton: the per-stage layout carrying an empty target list, a
    /// build section, and a publish section with empty sources. The optional test section
    /// is omitted to keep the skeleton minimal, and <c>build.preset</c> is written only when a
    /// preset was chosen. Every key matches a JsonPropertyName on the manifest model, so
    /// the emitted file parses strictly through <see cref="ManifestLoader.Parse"/>.
    /// </summary>
    /// <param name="preset">The preset name to seed, or null when none.</param>
    /// <returns>The complete file contents: pretty-printed manifest JSON indented with two spaces.</returns>
    private static string ComposeSkeleton(string? preset)
    {
        string skeleton = preset is null
            ? """
              {
                "targets": [],
                "build": {
                  "runtimeIdentifiers": []
                },
                "publish": {
                  "sources": []
                }
              }
              """
            : $$"""
              {
                "targets": [],
                "build": {
                  "preset": "{{preset}}",
                  "runtimeIdentifiers": []
                },
                "publish": {
                  "sources": []
                }
              }
              """;

        // The literal stops at the closing brace with no final newline: normalize line endings
        // for this OS and terminate the last line.
        return skeleton.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine;
    }
}
