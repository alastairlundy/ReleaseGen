namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// One manifest target: names one project and the release artifacts it produces.
/// </summary>
public sealed class ManifestTarget
{
    /// <summary>
    /// The path to the project file or directory. Relative paths resolve from the manifest's directory.
    /// </summary>
    [JsonPropertyName("project")]
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// The artifact kinds this target declares. Only declared kinds are produced.
    /// </summary>
    [JsonPropertyName("kinds")]
    public string[] Kinds { get; set; } = [];

    /// <summary>
    /// The archive formats for this target's executables-kind outputs: each entry is exactly
    /// 'zip', 'tar.gz', 'tar.xz', or 'none', no entry appears twice (a duplicate fails
    /// validation), and every declared format produces one archive per
    /// (target, rid). Absent means the rid-class default set (zip for Windows-class and any-*
    /// rids, zip plus tar.gz for Unix-based rids) - archiving is opt-out, not opt-in, and tar.xz
    /// is never a default. Package kinds are never archived regardless of this key.
    /// </summary>
    [JsonPropertyName("archiveFormats")]
    [JsonConverter(typeof(ArchiveFormatsConverter))]
    public string[] ArchiveFormats { get; set; } = [];
}
