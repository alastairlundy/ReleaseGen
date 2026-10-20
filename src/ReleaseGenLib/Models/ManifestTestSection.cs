namespace ReleaseGenLib.Models;

using System.Text.Json.Serialization;

/// <summary>
/// The test section: named test projects. When no projects are listed, the test stage
/// runs from the current directory.
/// </summary>
public sealed class ManifestTestSection
{
    /// <summary>
    /// The named test projects.
    /// </summary>
    [JsonPropertyName("projects")]
    public string[] Projects { get; set; } = [];
}
