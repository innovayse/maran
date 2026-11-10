using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Mappers;

/// <summary>
/// The agent is told a zone's signing in every publication: the mapper restates the zone's step and its key,
/// decrypted, as the instruction, and leaves it out only for a zone that is off.
/// </summary>
public sealed class ZoneDefinitionMapperSigningTests
{
    /// <summary>The instant everything happens at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Builds complete settings.</summary>
    /// <returns>The settings.</returns>
    private static DnsSettings Settings()
    {
        var settings = new DnsSettings(Now);
        settings.Save("ns1.panel.test", "ns2.panel.test", "hostmaster.panel.test", "203.0.113.5", string.Empty, 3600, Now);

        return settings;
    }

    /// <summary>Builds a zone, signed or not.</summary>
    /// <param name="signed">Whether to switch signing on.</param>
    /// <returns>The zone.</returns>
    private static Zone NewZone(bool signed)
    {
        var zone = new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.net", Now);
        if (signed)
        {
            zone.EnableSigning(Guid.NewGuid(), DnssecKeyPolicy.Generate(), Now);
        }

        return zone;
    }

    /// <summary>A signed zone is described with the signed phase and the whole stored key.</summary>
    [Fact]
    public void A_signed_zone_is_described_with_the_signed_phase_and_its_whole_key()
    {
        var zone = NewZone(signed: true);
        var key = zone.SigningKeys.Single();

        var definition = ZoneDefinitionMapper.From(zone, Settings());

        Assert.Equal(DnsSigningPhase.Active, definition.Signing!.Phase);
        Assert.Equal(key.PublicKey, definition.Signing.Key!.PublicKey);
        Assert.Equal(key.PrivateKey, definition.Signing.Key.PrivateKey.Reveal());
        Assert.Equal(key.Flags, definition.Signing.Key.Flags);
        Assert.Equal(key.KeyTag, definition.Signing.Key.KeyTag);
    }

    /// <summary>A zone being withdrawn is described with the withdrawing phase and the same key.</summary>
    [Fact]
    public void A_withdrawing_zone_is_described_with_the_withdrawing_phase_and_the_same_key()
    {
        var zone = NewZone(signed: true);
        zone.BeginWithdrawal(Now);

        var definition = ZoneDefinitionMapper.From(zone, Settings());

        Assert.Equal(DnsSigningPhase.Withdrawing, definition.Signing!.Phase);
        Assert.Equal(zone.SigningKeys.Single().PublicKey, definition.Signing.Key!.PublicKey);
    }

    /// <summary>Inverse control: a zone that is off is described with no signing instruction.</summary>
    [Fact]
    public void A_zone_that_is_off_is_described_with_no_signing_instruction()
    {
        Assert.Null(ZoneDefinitionMapper.From(NewZone(signed: false), Settings()).Signing);
    }

    /// <summary>
    /// A zone that is signed but was read without its key (a read that forgot the include) is refused loudly and
    /// never described as unsigned, which is what would publish it unsigned under a DS record.
    /// </summary>
    [Fact]
    public async Task A_signed_zone_read_without_its_key_is_refused_and_not_described_as_unsigned()
    {
        var (context, _) = await DnssecWorld.SigningAsync();
        using var __ = context;
        var zone = await context.Database.Zones.AsNoTracking().SingleAsync();
        var settings = await context.Database.DnsSettings.AsNoTracking().SingleAsync();

        Assert.Empty(zone.SigningKeys);
        Assert.Throws<InvalidOperationException>(() =>
        {
            return ZoneDefinitionMapper.From(zone, settings);
        });
    }

    /// <summary>Inverse control: the same zone read with its key is described, signed.</summary>
    [Fact]
    public async Task The_same_zone_read_with_its_key_is_described_as_signed()
    {
        var (context, _) = await DnssecWorld.SigningAsync();
        using var __ = context;
        var zone = await context.Database.Zones.AsNoTracking().Include(row => row.SigningKeys).SingleAsync();
        var settings = await context.Database.DnsSettings.AsNoTracking().SingleAsync();

        Assert.Equal(DnsSigningPhase.Active, ZoneDefinitionMapper.From(zone, settings).Signing!.Phase);
    }
}
