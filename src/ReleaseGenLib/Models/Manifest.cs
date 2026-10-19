namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The root manifest (D002): exactly one per repository, driving every stage.
/// The file is <c>releasegen.json</c>, parsed with System.Text.Json (T003); unknown fields fail
/// parse with an error naming the field. No manifest key ever holds a secret or a key reference
/// (D011): the model has no key-bearing fields, and strict parsing rejects anything else.
/// </summary>
public sealed class Manifest
{
    /// <summary>
    /// The manifest's targets: project paths plus declared artifact kinds (T007).
    /// A project may be named by more than one target (D013).
    /// </summary>
    [JsonPropertyName("targets")]
    public List<ManifestTarget> Targets { get; set; } = [];

    /// <summary>
    /// The build section: preset, runtime identifiers, and per-key flag overrides (T007, T013, T014).
    /// </summary>
    [JsonPropertyName("build")]
    public ManifestBuildSection Build { get; set; } = new();

    /// <summary>
    /// The optional test section; when absent, the publish stage proceeds with a warning (D004).
    /// </summary>
    [JsonPropertyName("test")]
    public ManifestTestSection? Test { get; set; }

    /// <summary>
    /// The publish section: NuGet source URLs (T007). Sources only - no credentials, ever (D011).
    /// </summary>
    [JsonPropertyName("publish")]
    public ManifestPublishSection Publish { get; set; } = new();
}
