using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Queries.GetZone;
using Maran.Modules.Dns.Queries.ListZones;
using Maran.Modules.Dns.Tests.TestSupport;

namespace Maran.Modules.Dns.Tests.Queries.GetZone;

/// <summary>A zone's DNSSEC step rides on the zone itself, in a read and in a list, so a screen can warn before it deletes a signed one.</summary>
public sealed class GetZoneDnssecStateTests
{
    /// <summary>A zone that was never signed reads as off in a read and in a list.</summary>
    [Fact]
    public async Task An_unsigned_zone_carries_the_off_state()
    {
        var (context, zoneId) = await DnssecWorld.OffAsync();
        using var _ = context;

        var read = await context.GetZone.HandleAsync(new GetZoneQuery(zoneId), CancellationToken.None);
        var listed = await context.ListZones.HandleAsync(new ListZonesQuery(), CancellationToken.None);

        Assert.Equal(DnssecState.Off, read.Value.DnssecState);
        Assert.Equal(DnssecState.Off, Assert.Single(listed.Value.Items).DnssecState);
    }

    /// <summary>A signed zone carries its step in a read and in a list.</summary>
    [Fact]
    public async Task A_signed_zone_carries_its_step()
    {
        var (context, zoneId) = await DnssecWorld.SigningAsync();
        using var _ = context;

        var signed = await context.GetZone.HandleAsync(new GetZoneQuery(zoneId), CancellationToken.None);
        var listedSigned = await context.ListZones.HandleAsync(new ListZonesQuery(), CancellationToken.None);

        Assert.Equal(DnssecState.Signing, signed.Value.DnssecState);
        Assert.Equal(DnssecState.Signing, Assert.Single(listedSigned.Value.Items).DnssecState);
    }

    /// <summary>A zone that is being withdrawn carries that step in a read and in a list.</summary>
    [Fact]
    public async Task A_withdrawing_zone_carries_its_step()
    {
        var (context, zoneId) = await DnssecWorld.WithdrawingAsync();
        using var _ = context;

        var read = await context.GetZone.HandleAsync(new GetZoneQuery(zoneId), CancellationToken.None);
        var listed = await context.ListZones.HandleAsync(new ListZonesQuery(), CancellationToken.None);

        Assert.Equal(DnssecState.Withdrawing, read.Value.DnssecState);
        Assert.Equal(DnssecState.Withdrawing, Assert.Single(listed.Value.Items).DnssecState);
    }
}
