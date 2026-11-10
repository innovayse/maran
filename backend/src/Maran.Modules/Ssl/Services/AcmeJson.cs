using System.Text.Json;

namespace Maran.Modules.Ssl.Services;

/// <summary>Reads the members of the JSON documents an ACME authority sends.</summary>
internal static class AcmeJson
{
    /// <summary>Reads one string member of a JSON object, or the empty string when it is absent.</summary>
    /// <param name="element">The object to read.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member's text, or the empty string.</returns>
    public static string Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
