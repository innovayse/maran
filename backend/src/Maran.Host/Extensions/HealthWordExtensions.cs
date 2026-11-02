using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Host.HealthChecks.Enums;

namespace Maran.Host.Extensions;

/// <summary>
/// The word an operator reads for a health state: exactly the spelling <c>/health</c> puts on the
/// wire, produced by the same converter and the same member-name attributes, so the console and the
/// endpoint can never name one state two ways.
/// </summary>
public static class HealthWordExtensions
{
    /// <summary>The serializer settings the wire uses for enums: the camelCase member name.</summary>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Names an agent state the way the wire does.</summary>
    /// <param name="state">The agent state.</param>
    /// <returns>The wire word, for example <c>connected</c>.</returns>
    public static string Word(this AgentHealth state)
    {
        return JsonSerializer.Serialize(state, WireOptions).Trim('"');
    }

    /// <summary>Names a database state the way the wire does.</summary>
    /// <param name="state">The database state.</param>
    /// <returns>The wire word, for example <c>no_schema</c>.</returns>
    public static string Word(this DatabaseHealth state)
    {
        return JsonSerializer.Serialize(state, WireOptions).Trim('"');
    }
}
