using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.SharedKernel.Security;

namespace Maran.Sdk.Serialization;

/// <summary>
/// The one definition of how the panel writes JSON on the wire: camel-case members, enums as their
/// camel-case names, a <see cref="SensitiveString"/> as its plain value, and every null written as
/// <c>null</c>.
/// </summary>
/// <remarks>
/// The Host applies <see cref="Apply"/> to both of its API surfaces (MVC controllers and minimal
/// endpoints) and <see cref="Wire"/> is what the server-sent-event stream serializes with, so a value
/// reaches the SPA with the same spelling whether it arrives in a response body or in a stream
/// frame. The SPA's shared types describe the panel's JSON once; a stream that left out the nulls a
/// response sends would make every such type lie about one of the two, and each reader would have to
/// restore what the other transport dropped. There is deliberately no ignore-null setting here:
/// "no value" is spelled <c>null</c> everywhere.
/// </remarks>
public static class PanelJsonOptions
{
    /// <summary>The options a payload is serialized with when it is not written by one of the Host's API surfaces.</summary>
    public static readonly JsonSerializerOptions Wire = CreateWire();

    /// <summary>Adds the panel's converters to the options of one API surface.</summary>
    /// <param name="options">The surface's serializer options, which start from the web defaults.</param>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new SensitiveStringJsonConverter());
    }

    /// <summary>Builds the stream's options from the same web defaults and converters an API surface gets.</summary>
    /// <returns>The options, ready to be shared.</returns>
    private static JsonSerializerOptions CreateWire()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        return options;
    }
}
