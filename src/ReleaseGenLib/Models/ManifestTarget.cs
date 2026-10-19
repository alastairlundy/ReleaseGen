namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// One manifest target: names one project and the release artifacts it produces (D014).
/// </summary>
public sealed class ManifestTarget
{
    /// <summary>
    /// The path to the project file or directory. Relative paths resolve from the manifest's directory.
    /// </summary>
    [JsonPropertyName("project")]
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// The artifact kinds this target declares. Only declared kinds are produced (D013).
    /// </summary>
    [JsonPropertyName("kinds")]
    public string[] Kinds { get; set; } = [];
}
