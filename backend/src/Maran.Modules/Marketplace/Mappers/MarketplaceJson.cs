using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>
/// The one reading and writing of the service's JSON. The service speaks camelCase with PascalCase enum
/// names; the panel reads names case-insensitively, ignores members it does not know (so the service can
/// add fields within a version), and refuses an answer missing a member it needs or carrying an
/// unrecognised enum word, because an answer it half understood is not one to act on.
/// </summary>
public static class MarketplaceJson
{
    /// <summary>The deepest nesting accepted, far above any real answer and far below a stack-exhausting one.</summary>
    private const int MaxDepth = 16;

    /// <summary>The shared options; read-only once first used.</summary>
    private static readonly JsonSerializerOptions Options = Build();

    /// <summary>Parses a response body into the expected shape.</summary>
    /// <typeparam name="T">The shape.</typeparam>
    /// <param name="utf8">The exact response bytes.</param>
    /// <returns>The parsed value, or null when the bytes are not that shape (malformed, a required member missing, an unknown enum word, or a JSON null).</returns>
    public static T? TryParse<T>(ReadOnlySpan<byte> utf8)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(utf8, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes a request body.</summary>
    /// <typeparam name="T">The shape.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    /// <summary>Builds the options once.</summary>
    /// <returns>The options.</returns>
    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            MaxDepth = MaxDepth,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new SensitiveStringJsonConverter());
        return options;
    }
}
