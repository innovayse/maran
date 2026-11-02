using System.Text.Json.Serialization;

namespace Maran.Host.HealthChecks.Enums;

/// <summary>
/// What the panel database is to the panel, as <see cref="DatabaseHealthProbe"/> measures it. On the
/// wire the words are <c>"reachable"</c>, <c>"no_schema"</c>, <c>"unreachable"</c> and
/// <c>"not_configured"</c>: the installer and the end-to-end scripts match them by value, so the
/// two snake_case spellings are pinned with <see cref="JsonStringEnumMemberNameAttribute"/> rather
/// than left to the camelCase policy.
/// </summary>
public enum DatabaseHealth
{
    /// <summary>A connection opens within the timeout and the panel's own schema is in the database.</summary>
    Reachable,

    /// <summary>
    /// The database answered, and the panel's own schema is NOT in it. A separate answer from
    /// <see cref="Unreachable"/> because the two need different actions from an operator, and because
    /// conflating them hid a defect for three releases: nothing applied the migrations, every fresh
    /// install came up with an empty database, and the probe reported it reachable the whole time
    /// (issue #66).
    /// </summary>
    [JsonStringEnumMemberName("no_schema")]
    NoSchema,

    /// <summary>The connection fails or times out.</summary>
    Unreachable,

    /// <summary>No connection string is configured at all (a shell run).</summary>
    [JsonStringEnumMemberName("not_configured")]
    NotConfigured,
}
