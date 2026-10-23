using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReleaseGenLib.Contexts;

/// <summary>
/// The source-generated System.Text.Json context for the manifest: strict defaults with
/// unknown fields failing parse, no inline comments, and no trailing commas. Source
/// generation keeps the trimming and AOT path open.
/// </summary>
[JsonSourceGenerationOptions(
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Manifest))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext;
