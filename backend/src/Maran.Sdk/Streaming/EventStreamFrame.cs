using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maran.Sdk.Streaming;

/// <summary>
/// Renders one server-sent event: its <c>event:</c> line, its JSON <c>data:</c> line, and the blank
/// line that terminates it.
/// </summary>
/// <remarks>
/// The payload is built by <see cref="JsonSerializer"/> and never by concatenation, and that is a
/// safety property rather than a style preference: the values a stream carries are customer file
/// content and caller-chosen names, so a value containing a newline would otherwise close the frame
/// early and let that content forge events of its own. JSON escaping keeps every value inside its
/// own frame, and the trailing blank line is what makes a browser dispatch the event at all.
/// </remarks>
public static class EventStreamFrame
{
    /// <summary>How the JSON payload of each event is written: camel case, with enums as their names.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Renders one event carrying a JSON payload.</summary>
    /// <param name="eventName">The event's name, as the SPA's stream reader dispatches on it.</param>
    /// <param name="payload">The value serialized as the event's data.</param>
    /// <returns>The event's wire text, terminated by the blank line that ends an event.</returns>
    public static string Render(string eventName, object payload)
    {
        var data = JsonSerializer.Serialize(payload, SerializerOptions);
        return $"event: {eventName}\ndata: {data}\n\n";
    }
}
