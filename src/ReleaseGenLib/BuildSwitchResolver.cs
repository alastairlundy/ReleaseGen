using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib;

using Models;

/// <summary>
/// Resolves the build preset in force plus the manifest build section's explicit keys into a
/// <see cref="PublishSwitchSet"/>. Pure logic - no I/O - so the preset-to-
/// switch mapping stays directly testable. Supersedes the legacy ProjectBuilder flag
/// mapping, including the inverted ReadyToRun check, rather than patching it.
/// </summary>
public static class BuildSwitchResolver
{
    /// <summary>
    /// Resolves the switch set for one (target, rid) publish.
    /// </summary>
    /// <param name="preset">The canonical preset name in force (flag override already applied by the CLI).</param>
    /// <param name="section">The manifest's build section; each present tri-state key overrides its preset default.</param>
    /// <param name="archiveFormat">The target's resolved archive format (declared or defaulted to zip); drives the Windows single-file default.</param>
    /// <param name="isWindows">Host OS override for the single-file OS rule; defaults to the current OS.</param>
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
                // Self-contained; trimming implied by AOT so no separate trimming switch.
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
                // Preset none: apply only what the build section specifies.
                publishAot = false;
                publishTrimmed = false;
                publishReadyToRun = false;
                selfContained = null;
                rollForward = null;
                break;
        }

        // Explicit manifest keys override the preset defaults (tri-state: present wins).
        if (section.PublishAot is { } publishAotOverride)
        {
            publishAot = publishAotOverride;
        }

        if (section.PublishTrimmed is { } publishTrimmedOverride)
        {
            publishTrimmed = publishTrimmedOverride;
        }

        if (section.PublishReadyToRun is { } publishReadyToRunOverride)
        {
            publishReadyToRun = publishReadyToRunOverride;
        }

        if (section.SelfContained is { } selfContainedOverride)
        {
            selfContained = selfContainedOverride;
        }

        // Single-file rules: the explicit key always wins; otherwise the OS
        // defaults apply for the four named presets - ON on Unix-based OSes, and on Windows only
        // when the target is zip-archived (declared or defaulted to zip). Preset none contributes
        // no implicit single-file switch, and default-off emits no switch so a project
        // file's own PublishSingleFile setting stays authoritative.
        bool? publishSingleFile;

        if (section.SingleFile is { } singleFileOverride)
        {
            publishSingleFile = singleFileOverride;
        }
        else
        {
            bool windows = isWindows ?? OperatingSystem.IsWindows();
            bool zipArchived = string.Equals(archiveFormat, ArchiveFormats.Zip, StringComparison.Ordinal);
            bool defaultOn = !string.Equals(preset, BuildPreset.None, StringComparison.OrdinalIgnoreCase) && (!windows || zipArchived);
            publishSingleFile = defaultOn ? true : null;
        }

        return new PublishSwitchSet(
            publishAot, publishTrimmed, publishReadyToRun, selfContained, rollForward, publishSingleFile);
    }
}
