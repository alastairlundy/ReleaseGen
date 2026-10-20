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
    /// The archive format for this target's executables-kind outputs: exactly 'zip',
    /// 'tar.gz', or 'none'. Absent means zip - archiving is opt-out, not opt-in. Package kinds
    /// are never archived regardless of this key.
    /// </summary>
    [JsonPropertyName("archive")]
    public string? Archive { get; set; }
}
