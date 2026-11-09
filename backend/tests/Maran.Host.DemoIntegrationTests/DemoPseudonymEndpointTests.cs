using System.Net;
using Maran.Host.DemoIntegrationTests.Fixtures;
using Npgsql;

namespace Maran.Host.DemoIntegrationTests;

/// <summary>The real address of a visitor is in no row of the session table or the audit journal.</summary>
[Collection(DemoSharedDatabase.Name)]
public sealed class DemoPseudonymEndpointTests
{
    /// <summary>The real address the visitor has.</summary>
    private const string RealAddress = "198.51.100.77";

    /// <summary>The shared server.</summary>
    private readonly DemoPostgres _postgres;

    /// <summary>Creates the class over the shared server.</summary>
    /// <param name="postgres">The shared server.</param>
    public DemoPseudonymEndpointTests(DemoPostgres postgres)
    {
        _postgres = postgres;
    }

    /// <summary>Reads one text column of every row of a table.</summary>
    /// <param name="connectionString">The host's database.</param>
    /// <param name="table">The quoted table.</param>
    /// <returns>The values, empty strings included.</returns>
    private static async Task<List<string>> AddressesAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select \"IpAddress\" from {table}", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return values;
    }

    /// <summary>
    /// After signing in, the session row and the audit rows hold the stand-in, and never the real address.
    /// </summary>
    [Fact]
    public async Task The_real_address_is_in_no_session_and_no_audit_row()
    {
        await using var demo = await SeededDemo.StartAsync(_postgres, peer: IPAddress.Parse(RealAddress));
        using var visitor = await demo.SignInAsync();

        var sessions = await AddressesAsync(demo.ConnectionString, "identity.\"Sessions\"");
        var audit = await AddressesAsync(demo.ConnectionString, "identity.\"AuditEvents\"");

        // The positive control: the probe must be able to see a stand-in, or "no real address" proves nothing.
        Assert.Contains(sessions, address => { return address.StartsWith("2001:db8:", StringComparison.Ordinal); });
        Assert.Contains(audit, address => { return address.StartsWith("2001:db8:", StringComparison.Ordinal); });
        Assert.DoesNotContain(sessions, address => { return address.Contains(RealAddress, StringComparison.Ordinal); });
        Assert.DoesNotContain(audit, address => { return address.Contains(RealAddress, StringComparison.Ordinal); });
    }
}
