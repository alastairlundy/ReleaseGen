namespace ReleaseGenLib.Models;

/// <summary>
/// The archive formats a manifest target may declare for its executables-kind outputs.
/// Exactly these four values are accepted in v1; anything else fails manifest validation
/// with an error naming the target and this set. When a target declares no
/// <c>archiveFormats</c> key, the effective formats resolve per (target, rid) through
/// <see cref="DefaultsFor"/> - archiving is opt-out, not opt-in.
/// </summary>
public static class ArchiveFormats
{
    /// <summary>
    /// Zip archive via System.IO.Compression.ZipFile. A member of every absent-key default
    /// set, so a target with no declared key always archives to zip.
    /// </summary>
    public const string Zip = "zip";

    /// <summary>
    /// Gzip-compressed tar archive via System.Formats.Tar plus GZipStream. The second member
    /// of the absent-key default set for Unix-based rid classes.
    /// </summary>
    public const string TarGz = "tar.gz";

    /// <summary>
    /// Xz-compressed tar archive. Strictly opt-in: never a default on any platform, and never
    /// produced for Windows-class or the portable <c>any-*</c> rids, which skip it per rid with
    /// one warning each (or fail manifest validation when it is the target's only declared
    /// format against those rids).
    /// </summary>
    public const string TarXz = "tar.xz";

    /// <summary>
    /// No archive: the per-rid output folder stays unarchived.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// The full set of archive formats valid in a manifest.
    /// </summary>
    public static readonly string[] All = [Zip, TarGz, TarXz, None];

    /// <summary>
    /// Returns whether <paramref name="format"/> is a declared archive format valid in v1.
    /// </summary>
    /// <param name="format">The target's 'archiveFormats' value.</param>
    public static bool IsKnown(string format) => format is Zip or TarGz or TarXz or None;

    /// <summary>
    /// The absent-key default format set for one rid: zip alone for Windows-class and the
    /// portable <c>any-*</c> rids, zip plus tar.gz for Unix-based rids. tar.xz is never a
    /// default on any platform - it is always an explicit opt-in. An explicit
    /// <c>archiveFormats</c> key overrides this set entirely.
    /// </summary>
    /// <param name="rid">The runtime identifier the archives are produced for.</param>
    /// <returns>The default format set, one entry per format the rid produces by default.</returns>
    public static string[] DefaultsFor(string rid) =>
        SupportsTarXz(rid) ? [Zip, TarGz] : [Zip];

    /// <summary>
    /// Whether tar.xz may be produced for <paramref name="rid"/>. Windows-class rids
    /// (<c>win-*</c>) and the portable <c>any-*</c> rids follow the Windows rule and never
    /// produce tar.xz, regardless of the host OS; every other rid class is Unix-based and
    /// may produce it. Explicit <c>tar.gz</c> keys stay honored for every rid class.
    /// </summary>
    /// <param name="rid">The runtime identifier the archives are produced for.</param>
    /// <returns><see langword="true"/> when tar.xz is producible for the rid.</returns>
    public static bool SupportsTarXz(string rid) =>
        !rid.StartsWith("win", StringComparison.OrdinalIgnoreCase) &&
        !rid.StartsWith("any-", StringComparison.OrdinalIgnoreCase);
}
