using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Commands.EnableZoneSigning;
using Maran.Modules.Dns.Commands.WithdrawZoneSigning;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// Builds the worlds the DNSSEC tests start from: an administrator's panel holding one zone of one account, in
/// each step of its DNSSEC life. Each step is reached through the real handlers, never by writing the row.
/// </summary>
public static class DnssecWorld
{
    /// <summary>The account that owns the zone.</summary>
    public static readonly Guid AccountId = new("55555555-5555-4555-8555-555555555555");

    /// <summary>The zone's apex name.</summary>
    public const string ZoneName = "example.net";

    /// <summary>A world whose one zone is not signed, on a host that can sign.</summary>
    /// <returns>The world and the zone's id.</returns>
    public static async Task<(DnsTestContext Context, Guid ZoneId)> OffAsync()
    {
        var context = new DnsTestContext();
        context.Accounts.Add(AccountId, "erin", maxDnsZones: 5);
        await context.EnableDnsServerAsync();
        var created = await context.CreateZone.HandleAsync(
            new CreateZoneCommand(AccountId, ZoneName, "203.0.113.1", "tests"), CancellationToken.None);
        Assert.True(created.IsSuccess);

        return (context, created.Value.Id);
    }

    /// <summary>A world whose one zone has been signed.</summary>
    /// <returns>The world and the zone's id.</returns>
    public static async Task<(DnsTestContext Context, Guid ZoneId)> SigningAsync()
    {
        var (context, zoneId) = await OffAsync();
        var enabled = await context.EnableSigning.HandleAsync(
            new EnableZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        Assert.True(enabled.IsSuccess);

        return (context, zoneId);
    }

    /// <summary>A world whose one zone is being withdrawn from signing, the withdrawal having just begun.</summary>
    /// <returns>The world and the zone's id.</returns>
    public static async Task<(DnsTestContext Context, Guid ZoneId)> WithdrawingAsync()
    {
        var (context, zoneId) = await SigningAsync();
        var withdrawn = await context.WithdrawSigning.HandleAsync(
            new WithdrawZoneSigningCommand(zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        Assert.True(withdrawn.IsSuccess);

        return (context, zoneId);
    }
}
