namespace ReleaseGenLib.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads a target's <c>archiveFormats</c> key as either a bare string (<c>"zip"</c>) or a JSON
/// array (<c>["zip","tar.gz"]</c>), normalizing both to one string list so the manifest type
/// stays a plain <c>string[]</c> inside the strict source-generated parse. Every other token
/// shape - a number, an object, a null, or a non-string array entry - fails parse with an error
/// naming the key, so a malformed value never silently degrades to the default.
/// </summary>
public sealed class ArchiveFormatsConverter : JsonConverter<string[]>
{
    /// <summary>
    /// The manifest key this converter reads, named in every rejection.
    /// </summary>
    private const string Key = "archiveFormats";

    /// <summary>
    /// The converter also sees the JSON null token: an explicit <c>"archiveFormats": null</c>
    /// is a malformed value (absence is the way to accept the default), not an absent key.
    /// </summary>
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return [reader.GetString() ?? string.Empty];

            case JsonTokenType.StartArray:
                List<string> formats = [];

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return [.. formats];
                    }

                    if (reader.TokenType != JsonTokenType.String)
                    {
                        throw Malformed();
                    }

                    formats.Add(reader.GetString() ?? string.Empty);
                }

                throw Malformed();

            default:
                throw Malformed();
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (string format in value)
        {
            writer.WriteStringValue(format);
        }

        writer.WriteEndArray();
    }

    private static JsonException Malformed() =>
        new($"The JSON key '{Key}' must be a string or an array of strings.");
}
