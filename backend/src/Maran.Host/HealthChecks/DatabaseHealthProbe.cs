using Npgsql;

namespace Maran.Host.HealthChecks;

/// <summary>
/// Checks that the panel database answers. Readiness depends on it: without the database the panel
/// can neither authenticate anyone nor queue work, so it must not be sent traffic.
/// </summary>
public sealed class DatabaseHealthProbe
{
    /// <summary>Reported when a connection opens within the timeout.</summary>
    public const string Reachable = "reachable";

    /// <summary>Reported when the connection fails or times out.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>Reported when no connection string is configured at all (a shell run).</summary>
    public const string NotConfigured = "not_configured";

    /// <summary>How long opening a connection may take before the database counts as unreachable.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The panel database connection string, empty when none is configured.</summary>
    private readonly string _connectionString;

    /// <summary>Creates the probe for a connection string.</summary>
    /// <param name="connectionString">The panel database connection string; may be empty.</param>
    public DatabaseHealthProbe(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Opens a short-lived connection and reports the outcome.</summary>
    /// <returns><see cref="Reachable"/>, <see cref="Unreachable"/> or <see cref="NotConfigured"/>.</returns>
    public async Task<string> ProbeAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return NotConfigured;
        }

        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cts.Token);
            return Reachable;
        }
        catch (Exception)
        {
            // A readiness probe reports state; it never throws. Diagnosing *why* the database is
            // unreachable is the log's job, not the probe's.
            return Unreachable;
        }
    }
}
