using Npgsql;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Turns a container's connection string into the panel's individual <c>Database:*</c> settings.
/// The panel deliberately takes the database as separate keys rather than one string, so a test
/// host has to be configured the same way — otherwise the tests would exercise a configuration
/// shape that production does not have.
/// </summary>
public static class DatabaseSettings
{
    /// <summary>Maps a connection string to the settings a test host should be given.</summary>
    /// <param name="connectionString">Connection string produced by the test container.</param>
    /// <returns>Configuration key/value pairs to apply with <c>UseSetting</c>.</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> From(string connectionString)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        return
        [
            new KeyValuePair<string, string>("Database:Host", parsed.Host ?? "localhost"),
            new KeyValuePair<string, string>("Database:Port", parsed.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("Database:Database", parsed.Database ?? string.Empty),
            new KeyValuePair<string, string>("Database:Username", parsed.Username ?? string.Empty),
            new KeyValuePair<string, string>("Database:Password", parsed.Password ?? string.Empty),
        ];
    }
}
