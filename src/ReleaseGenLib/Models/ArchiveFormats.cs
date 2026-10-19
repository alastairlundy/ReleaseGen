namespace ReleaseGenLib.Models;

/// <summary>
/// The archive formats a manifest target may declare for its executables-kind outputs (T016).
/// Exactly these three values are accepted in v1; anything else fails manifest validation
/// with an error naming the target and this set (D009).
/// </summary>
public static class ArchiveFormats
{
    /// <summary>
    /// Zip archive via System.IO.Compression.ZipFile. The default when a target declares no
    /// archive key (opt-out, not opt-in).
    /// </summary>
    public const string Zip = "zip";

    /// <summary>
    /// Gzip-compressed tar archive via System.Formats.Tar plus GZipStream.
    /// </summary>
    public const string TarGz = "tar.gz";

    /// <summary>
    /// No archive: the per-rid output folder stays unarchived.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// The full set of archive formats valid in a manifest (T016).
    /// </summary>
    public static readonly string[] All = [Zip, TarGz, None];

    /// <summary>
    /// Returns whether <paramref name="format"/> is a declared archive format valid in v1.
    /// </summary>
    /// <param name="format">The target's 'archive' value.</param>
    public static bool IsKnown(string format) => format is Zip or TarGz or None;
}
