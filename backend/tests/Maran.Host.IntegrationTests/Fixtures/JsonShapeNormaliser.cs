using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Reduces a JSON response to its contract: property names, nesting and value kinds, with what varies
/// from one run to the next replaced by a placeholder and every object's properties in a fixed order.
/// What is left is the text a golden file holds and a diff can be read from.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is replaced, and why it is safe to.</b> A GUID becomes <c>&lt;guid&gt;</c> and an ISO-8601
/// timestamp <c>&lt;timestamp&gt;</c>, because the contract is "this is an id" and "this is a time", not
/// which one. The <c>correlationId</c> of a problem document becomes <c>&lt;correlationId&gt;</c> (it is
/// minted per request), and its <c>title</c> and <c>detail</c> become <c>&lt;text&gt;</c> (they are the
/// caller's-language sentence, whose wording is the resource files' business and is pinned elsewhere).
/// </para>
/// <para>
/// <b>What is deliberately kept.</b> Every other string, number, boolean and null, so a status moving
/// from <c>"active"</c> to <c>"Active"</c>, a field turning from a string into null, or an id turning
/// into a number all change the golden.
/// </para>
/// </remarks>
public static class JsonShapeNormaliser
{
    /// <summary>What an ISO-8601 timestamp with an offset looks like.</summary>
    private static readonly Regex Timestamp = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Property names whose string value is replaced wholesale, with the placeholder that replaces it.</summary>
    private static readonly Dictionary<string, string> Placeholders = new Dictionary<string, string>
    {
        ["correlationId"] = "<correlationId>",
        ["title"] = "<text>",
        ["detail"] = "<text>",
    };

    /// <summary>Normalises a JSON document.</summary>
    /// <param name="json">The document, as the server wrote it.</param>
    /// <returns>The normalised document: indented, LF line endings, properties sorted ordinally, no trailing newline.</returns>
    public static string Normalise(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            Write(writer, document.RootElement, null);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes one element, recursing into objects and arrays.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="element">The element to write.</param>
    /// <param name="propertyName">The name of the property holding the element, or null at the root and in arrays.</param>
    private static void Write(Utf8JsonWriter writer, JsonElement element, string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject().OrderBy(candidate => { return candidate.Name; }, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, property.Name);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item, null);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(Placeholder(propertyName, element.GetString()!));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    /// <summary>Chooses what a string value is written as.</summary>
    /// <param name="propertyName">The property holding the value, or null.</param>
    /// <param name="value">The value.</param>
    /// <returns>A placeholder for a value that varies per run, otherwise the value itself.</returns>
    private static string Placeholder(string? propertyName, string value)
    {
        if (propertyName is not null && Placeholders.TryGetValue(propertyName, out var fixedText))
        {
            return fixedText;
        }

        if (Guid.TryParse(value, out _))
        {
            return "<guid>";
        }

        if (Timestamp.IsMatch(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out _))
        {
            return "<timestamp>";
        }

        return value;
    }
}
