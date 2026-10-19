namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The publish section (T007): NuGet source URLs. Empty means the nuget.org default (D011).
/// </summary>
public sealed class ManifestPublishSection
{
    /// <summary>
    /// The NuGet source URLs to publish to.
    /// </summary>
    [JsonPropertyName("sources")]
    public string[] Sources { get; set; } = [];
}
