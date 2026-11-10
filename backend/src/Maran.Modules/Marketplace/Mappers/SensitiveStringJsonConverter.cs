using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>
/// Reads a JSON string straight into a <see cref="SensitiveString"/>, so a grant id or address is never
/// held as a plain string field of a record that a log line could print. It only reads: nothing in the
/// module writes a grant back out, and a converter that wrote the value would be a way to put it on
/// the wire by accident.
/// </summary>
public sealed class SensitiveStringJsonConverter : JsonConverter<SensitiveString>
{
    /// <summary>Reads a JSON string token.</summary>
    /// <param name="reader">The reader, positioned on the token.</param>
    /// <param name="typeToConvert">Always <see cref="SensitiveString"/>.</param>
    /// <param name="options">The serializer options; unused.</param>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="JsonException">The token is not a string.</exception>
    public override SensitiveString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A sensitive value must be a JSON string.");
        }

        return new SensitiveString(reader.GetString() ?? string.Empty);
    }

    /// <summary>Refuses to write the value: the module never serialises a grant.</summary>
    /// <param name="writer">The writer; unused.</param>
    /// <param name="value">The value; never written.</param>
    /// <param name="options">The serializer options; unused.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(Utf8JsonWriter writer, SensitiveString value, JsonSerializerOptions options)
    {
        throw new NotSupportedException("A sensitive value is never written to JSON.");
    }
}
