namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The build section (T007, T013, T014): the global preset key, the rid list, and per-key flag
/// overrides. Nullable flags are tri-state: absent means the manifest does not override.
/// </summary>
public sealed class ManifestBuildSection
{
    /// <summary>
    /// The global build preset key (T013). Absent or empty means preset none.
    /// </summary>
    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    /// <summary>
    /// The rid list the build stage publishes once per rid (T009).
    /// </summary>
    [JsonPropertyName("runtimeIdentifiers")]
    public string[] RuntimeIdentifiers { get; set; } = [];

    /// <summary>
    /// Per-key flag override: publish AOT.
    /// </summary>
    [JsonPropertyName("publishAot")]
    public bool? PublishAot { get; set; }

    /// <summary>
    /// Per-key flag override: publish trimmed.
    /// </summary>
    [JsonPropertyName("publishTrimmed")]
    public bool? PublishTrimmed { get; set; }

    /// <summary>
    /// Per-key flag override: publish ready-to-run.
    /// </summary>
    [JsonPropertyName("readyToRun")]
    public bool? PublishReadyToRun { get; set; }

    /// <summary>
    /// Per-key flag override: self-contained.
    /// </summary>
    [JsonPropertyName("selfContained")]
    public bool? SelfContained { get; set; }

    /// <summary>
    /// The singleFile opt-out key (T014): <c>false</c> disables the single-file defaults;
    /// absent leaves T010's OS rules in force.
    /// </summary>
    [JsonPropertyName("singleFile")]
    public bool? SingleFile { get; set; }
}
