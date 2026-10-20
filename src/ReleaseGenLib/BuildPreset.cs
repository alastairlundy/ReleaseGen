namespace ReleaseGenLib;

/// <summary>
/// The canonical build preset names a manifest's <c>build.preset</c> key may hold.
/// The set lives here so the init stage and the build stage resolve the same
/// names: unknown names fail with an error listing this set, and <see cref="None"/>
/// (like an absent key) means preset none.
/// </summary>
public static class BuildPreset
{
    /// <summary>
    /// Preset none: no preset key in the manifest; per-key defaults apply.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// Native AOT plus assembly trimming.
    /// </summary>
    public const string AotPlusTrimming = "aot-plus-trimming";

    /// <summary>
    /// Ready-to-run compilation.
    /// </summary>
    public const string ReadyToRun = "ready-to-run";

    /// <summary>
    /// Assembly trimming only.
    /// </summary>
    public const string TrimmingOnly = "trimming-only";

    /// <summary>
    /// Framework-dependent deployment.
    /// </summary>
    public const string FrameworkDependent = "framework-dependent";

    /// <summary>
    /// The full set of known preset names, in canonical spelling.
    /// </summary>
    public static readonly string[] All = [AotPlusTrimming, ReadyToRun, TrimmingOnly, FrameworkDependent, None];

    /// <summary>
    /// Returns whether <paramref name="name"/> is a known preset name (canonical spelling, ordinal match).
    /// </summary>
    /// <param name="name">The candidate preset name.</param>
    public static bool IsKnown(string? name) => TryParse(name, out _);

    /// <summary>
    /// Attempts to resolve <paramref name="name"/> to its canonical spelling. Callers
    /// report a failed parse by naming the unknown value alongside <see cref="All"/>.
    /// </summary>
    /// <param name="name">The candidate preset name.</param>
    /// <param name="preset">The canonical preset name when known; empty string otherwise.</param>
    /// <returns>true when <paramref name="name"/> is a known preset.</returns>
    public static bool TryParse(string? name, out string preset)
    {
        foreach (string candidate in All)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
            {
                preset = candidate;
                return true;
            }
        }

        preset = string.Empty;
        return false;
    }
}
