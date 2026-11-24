using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReleaseGen.Cli.Contexts;

/// <summary>
/// The source-generated System.Text.Json context the CLI's own serialization needs. It exists
/// so <c>InitCommand</c> can quote and escape manifest string literals through the generated
/// <c>JsonTypeInfo&lt;string&gt;</c> instead of the reflection-based
/// <c>JsonSerializer.Serialize</c>, which carries <c>RequiresUnreferencedCode</c> and breaks the
/// trimming path. Source generation keeps the AOT and trim path open.
/// </summary>
[JsonSourceGenerationOptions]
[JsonSerializable(typeof(string))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
