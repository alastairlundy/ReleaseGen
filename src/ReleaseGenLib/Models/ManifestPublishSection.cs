namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The publish section: NuGet source URLs. Empty means the nuget.org default.
/// </summary>
public sealed class ManifestPublishSection
{
    /// <summary>
    /// The NuGet source URLs to publish to.
    /// </summary>
    [JsonPropertyName("sources")]
    public string[] Sources { get; set; } = [];

    /// <summary>
    /// Standing answer for duplicate pushes, boolean with default false. The C# type is
    /// nullable so an absent key (null) stays distinguishable from an explicit false:
    /// absent behaves downstream exactly as today's fail-fast, while an explicit value
    /// feeds the manifest side of the skip-duplicates merge.
    /// Null never serializes, so a generated manifest never emits "skipDuplicates": null.
    /// </summary>
    [JsonPropertyName("skipDuplicates")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SkipDuplicates { get; set; }
}
