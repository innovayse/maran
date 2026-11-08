using Maran.Agent.Client.Services.DnsService;
using Maran.Agent.Simulated.Services.DnsService;

namespace Maran.Agent.Simulated.Tests.Services.DnsService;

/// <summary>What a simulated server answers to the DNS contract: a name server that keeps zones in memory.</summary>
public sealed class SimulatedAgentDnsClientTests
{
    /// <summary>A zone with no records, enough for the simulator, which only remembers names and serials.</summary>
    private static readonly DnsZoneDefinition Zone =
        new("example.com", 2026110701, "ns1.example.net", "hostmaster.example.net", ["ns1.example.net"], []);

    /// <summary>Enabling starts the daemon and disabling stops it, as the status reports.</summary>
    [Fact]
    public async Task The_daemon_runs_after_enable_and_stops_after_disable()
    {
        var client = new SimulatedAgentDnsClient();

        var off = await client.GetStatusAsync(CancellationToken.None);
        var on = await client.EnableAsync(CancellationToken.None);
        var stopped = await client.DisableAsync(CancellationToken.None);

        Assert.False(off.Value.Running);
        Assert.True(on.Value.Running);
        Assert.True(on.Value.Listening);
        Assert.False(stopped.Value.Running);
    }

    /// <summary>A put zone is served, listed and read back with its serial, and a delete removes it.</summary>
    [Fact]
    public async Task A_put_zone_is_served_listed_and_removed_by_a_delete()
    {
        var client = new SimulatedAgentDnsClient();

        await client.PutZoneAsync(Zone, CancellationToken.None);
        var read = await client.GetZoneAsync("example.com", CancellationToken.None);
        var listed = await client.ListZonesAsync(CancellationToken.None);
        var removed = await client.DeleteZoneAsync("example.com", CancellationToken.None);
        var gone = await client.GetZoneAsync("example.com", CancellationToken.None);

        Assert.Equal(2026110701L, read.Value.Serial);
        Assert.Equal(["example.com"], listed.Value);
        Assert.True(removed.IsSuccess);
        Assert.False(gone.IsSuccess);
        Assert.Equal("AgentNotFound", gone.Error!.Code);
    }

    /// <summary>The control for the refusals: a zone never put answers not found, and deleting it says so.</summary>
    [Fact]
    public async Task A_zone_never_put_is_not_found_and_cannot_be_deleted()
    {
        var client = new SimulatedAgentDnsClient();

        var read = await client.GetZoneAsync("absent.example", CancellationToken.None);
        var removed = await client.DeleteZoneAsync("absent.example", CancellationToken.None);

        Assert.Equal("AgentNotFound", read.Error!.Code);
        Assert.Equal("AgentNotFound", removed.Error!.Code);
    }
}
