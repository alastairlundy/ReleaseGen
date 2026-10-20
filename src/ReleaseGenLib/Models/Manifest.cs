namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The root manifest: exactly one per repository, driving every stage.
/// The file is <c>releasegen.json</c>, parsed with System.Text.Json; unknown fields fail
/// parse with an error naming the field. No manifest key ever holds a secret or a key
/// reference: the model has no key-bearing fields, and strict parsing rejects anything else.
/// </summary>
public sealed class Manifest
{
    /// <summary>
    /// The manifest's targets: project paths plus declared artifact kinds.
    /// A project may be named by more than one target.
    /// </summary>
    [JsonPropertyName("targets")]
    public List<ManifestTarget> Targets { get; set; } = [];

    /// <summary>
    /// The build section: preset, runtime identifiers, and per-key flag overrides.
    /// </summary>
    [JsonPropertyName("build")]
    public ManifestBuildSection Build { get; set; } = new();

    /// <summary>
    /// The optional test section; when absent, the publish stage proceeds with a warning.
    /// </summary>
    [JsonPropertyName("test")]
    public ManifestTestSection? Test { get; set; }

    /// <summary>
    /// The publish section: NuGet source URLs. Sources only - no credentials, ever.
    /// </summary>
    [JsonPropertyName("publish")]
    public ManifestPublishSection Publish { get; set; } = new();
}
