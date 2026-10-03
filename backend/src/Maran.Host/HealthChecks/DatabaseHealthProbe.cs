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

    /// <summary>
    /// The database answered, and the panel's own schema is NOT in it.
    /// </summary>
    /// <remarks>
    /// A separate answer from <see cref="Unreachable"/> because the two need different actions from
    /// an operator and because conflating them hid a defect for three releases: nothing applied the
    /// migrations, every fresh install came up with an empty database, and this probe reported
    /// <see cref="Reachable"/> the whole time — it had opened a connection and asked nothing else
    /// (issue #66). Every check in the project reads this field, so a probe that cannot tell an empty
    /// database from a working one makes every one of them blind to it.
    /// </remarks>
    public const string NoSchema = "no_schema";

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
    /// <returns><see cref="Reachable"/>, <see cref="NoSchema"/>, <see cref="Unreachable"/> or <see cref="NotConfigured"/>.</returns>
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

            // Opening a connection says the server is up; it says nothing about whether this panel
            // can work. `identity."Users"` is the narrowest honest proof that it can: without that
            // table no operator can sign in, and it is the first thing a fresh install touches.
            // `to_regclass` returns null rather than raising when the relation is absent, so this
            // asks the question without an exception as the answer.
            // Cast to text, because `to_regclass` returns the `regclass` type and Npgsql has no
            // reader for it — the call throws, the catch below turns that into "unreachable", and a
            // working database reports as a broken one. Measured: the same query runs fine in psql.
            await using var command = new NpgsqlCommand("SELECT to_regclass('identity.\"Users\"')::text", connection);
            var relation = await command.ExecuteScalarAsync(cts.Token);
            return relation is null or DBNull ? NoSchema : Reachable;
        }
        catch (Exception exception)
        {
            // A readiness probe reports state; it never throws. But it must SAY what went wrong, and
            // this `catch` said nothing for as long as it existed: a defect in the probe's OWN query
            // was reported as "the database is unreachable" on a database that was perfectly healthy,
            // with no trace anywhere. Diagnosing it took reading the source rather than the log,
            // which is exactly backwards.
            //
            // Serilog's static logger rather than an injected one: this type is constructed with a
            // connection string and nothing else, by a health-check registration that has no
            // container yet, and a probe is not worth reshaping the registration for.
            Serilog.Log.Warning(exception, "The database readiness probe failed; reporting {State}.", Unreachable);
            return Unreachable;
        }
    }
}
