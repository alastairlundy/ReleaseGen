namespace ReleaseGenLib;

using ReleaseGenLib.Models;

/// <summary>
/// The fully resolved dotnet publish switch set for one (target, rid) publish (T009, T010).
/// Preset defaults are resolved first, then any tri-state key present in the manifest's build
/// section overrides its preset default (T013); preset none contributes nothing implicit.
/// </summary>
/// <param name="PublishAot">Whether <c>--publish-aot</c> is in force.</param>
/// <param name="PublishTrimmed">Whether trimming is in force (rendered only when AOT is not, since AOT implies trimming - T010).</param>
/// <param name="PublishReadyToRun">Whether ready-to-run compilation is in force.</param>
/// <param name="SelfContained">Self-contained state; null means no <c>--self-contained</c> switch (preset none with no manifest key).</param>
/// <param name="RollForward">Roll-forward policy for framework-dependent publishes; null means no switch.</param>
/// <param name="PublishSingleFile">Single-file state; null means no switch - the default-off case leaves the project file's own setting alone (T010, T014).</param>
public sealed record PublishSwitchSet(
    bool PublishAot,
    bool PublishTrimmed,
    bool PublishReadyToRun,
    bool? SelfContained,
    string? RollForward,
    bool? PublishSingleFile)
{
    /// <summary>
    /// Renders the switch set as dotnet publish arguments, in a stable order. Only enabling
    /// switches are emitted for the boolean build flags (plus the explicitly valued
    /// <c>--self-contained</c> and single-file switches), so a project file's own settings stay
    /// authoritative whenever ReleaseGen has no opinion.
    /// </summary>
    /// <returns>The publish switches for this (target, rid).</returns>
    public IReadOnlyList<string> ToArguments()
    {
        List<string> arguments = [];

        if (PublishAot)
        {
            // Property form, not the nonexistent `--publish-aot` CLI switch: dotnet publish has
            // no aot switch (--help lists none) and forwards unknown switches to MSBuild, which
            // rejects them with MSB1001. PublishAot is the SDK property behind AOT publishing.
            arguments.Add("-p:PublishAot=true");
        }

        // AOT implies trimming: never a separate trimming switch alongside publish-aot (T010).
        if (PublishTrimmed && !PublishAot)
        {
            arguments.Add("-p:PublishTrimmed=true");
        }

        if (PublishReadyToRun)
        {
            arguments.Add("-p:PublishReadyToRun=true");
        }

        if (SelfContained is bool selfContained)
        {
            arguments.Add($"--self-contained={(selfContained ? "true" : "false")}");
        }

        if (RollForward is not null)
        {
            arguments.Add($"-p:RollForward={RollForward}");
        }

        if (PublishSingleFile is bool singleFile)
        {
            arguments.Add($"-p:PublishSingleFile={(singleFile ? "true" : "false")}");
        }

        return arguments;
    }
}

/// <summary>
/// Resolves the build preset in force plus the manifest build section's explicit keys into a
/// <see cref="PublishSwitchSet"/> (T010, T013, T014). Pure logic - no I/O - so the preset-to-
/// switch mapping stays directly testable (T018). Supersedes the legacy ProjectBuilder flag
/// mapping, including the inverted ReadyToRun check, rather than patching it (T009).
/// </summary>
public static class BuildSwitchResolver
{
    /// <summary>
    /// Resolves the switch set for one (target, rid) publish.
    /// </summary>
    /// <param name="preset">The canonical preset name in force (flag override already applied by the CLI - T013).</param>
    /// <param name="section">The manifest's build section; each present tri-state key overrides its preset default.</param>
    /// <param name="archiveFormat">The target's resolved archive format (declared or defaulted to zip - T016); drives the Windows single-file default (T010).</param>
    /// <param name="isWindows">Host OS override for the single-file OS rule; defaults to the current OS (T010).</param>
    /// <returns>The resolved switch set.</returns>
    /// <exception cref="BuildStageException"><paramref name="preset"/> is not a known preset name.</exception>
    public static PublishSwitchSet Resolve(
        string preset,
        ManifestBuildSection section,
        string archiveFormat,
        bool? isWindows = null)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (!BuildPreset.IsKnown(preset))
        {
            throw new BuildStageException(
                $"Unknown build preset '{preset}'. Known presets: {string.Join(", ", BuildPreset.All)}.");
        }

        // Preset defaults. null/false means the preset expresses no opinion for that flag.
        bool publishAot;
        bool publishTrimmed;
        bool publishReadyToRun;
        bool? selfContained;
        string? rollForward;

        switch (preset)
        {
            case BuildPreset.AotPlusTrimming:
                // Self-contained; trimming implied by AOT so no separate trimming switch (T010).
                publishAot = true;
                publishTrimmed = true;
                publishReadyToRun = false;
                selfContained = true;
                rollForward = null;
                break;

            case BuildPreset.ReadyToRun:
                publishAot = false;
                publishTrimmed = false;
                publishReadyToRun = true;
                selfContained = true;
                rollForward = null;
                break;

            case BuildPreset.TrimmingOnly:
                publishAot = false;
                publishTrimmed = true;
                publishReadyToRun = false;
                selfContained = true;
                rollForward = null;
                break;

            case BuildPreset.FrameworkDependent:
                publishAot = false;
                publishTrimmed = false;
                publishReadyToRun = false;
                selfContained = false;
                rollForward = "Minor";
                break;

            default:
                // Preset none: apply only what the build section specifies (T013).
                publishAot = false;
                publishTrimmed = false;
                publishReadyToRun = false;
                selfContained = null;
                rollForward = null;
                break;
        }

        // Explicit manifest keys override the preset defaults (tri-state: present wins - T013).
        if (section.PublishAot is bool publishAotOverride)
        {
            publishAot = publishAotOverride;
        }

        if (section.PublishTrimmed is bool publishTrimmedOverride)
        {
            publishTrimmed = publishTrimmedOverride;
        }

        if (section.PublishReadyToRun is bool publishReadyToRunOverride)
        {
            publishReadyToRun = publishReadyToRunOverride;
        }

        if (section.SelfContained is bool selfContainedOverride)
        {
            selfContained = selfContainedOverride;
        }

        // Single-file rules (T010, T014, T016): the explicit key always wins; otherwise the OS
        // defaults apply for the four named presets - ON on Unix-based OSes, and on Windows only
        // when the target is zip-archived (declared or defaulted to zip). Preset none contributes
        // no implicit single-file switch (T013), and default-off emits no switch so a project
        // file's own PublishSingleFile setting stays authoritative.
        bool? publishSingleFile;

        if (section.SingleFile is bool singleFileOverride)
        {
            publishSingleFile = singleFileOverride;
        }
        else
        {
            bool windows = isWindows ?? OperatingSystem.IsWindows();
            bool zipArchived = string.Equals(archiveFormat, ArchiveFormats.Zip, StringComparison.Ordinal);
            bool defaultOn = preset != BuildPreset.None && (!windows || zipArchived);
            publishSingleFile = defaultOn ? true : null;
        }

        return new PublishSwitchSet(
            publishAot, publishTrimmed, publishReadyToRun, selfContained, rollForward, publishSingleFile);
    }
}
