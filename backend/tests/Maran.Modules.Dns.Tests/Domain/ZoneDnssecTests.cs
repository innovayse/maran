using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain;

/// <summary>
/// The zone's DNSSEC life is a state machine owned by the zone: off, signing, withdrawing, off again. Every
/// step is a method, every accepted step moves the serial, and every refused step leaves the zone as it was.
/// </summary>
public sealed class ZoneDnssecTests
{
    /// <summary>The instant the zone is created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Builds an unsigned zone.</summary>
    /// <returns>The zone.</returns>
    private static Zone NewZone()
    {
        return new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.net", Created);
    }

    /// <summary>Moves a zone to the signing step.</summary>
    /// <param name="zone">The zone, off.</param>
    /// <param name="at">When.</param>
    private static void Sign(Zone zone, DateTimeOffset at)
    {
        Assert.Equal(DnssecTransitionVerdict.Accepted, zone.EnableSigning(Guid.NewGuid(), DnssecKeyPolicy.Generate(), at));
    }

    /// <summary>A new zone is off, holds no key and has no withdrawal under way.</summary>
    [Fact]
    public void A_new_zone_is_off_with_no_key()
    {
        var zone = NewZone();

        Assert.Equal(DnssecState.Off, zone.DnssecState);
        Assert.Empty(zone.SigningKeys);
        Assert.Null(zone.DnssecWithdrawalStartedAt);
    }

    /// <summary>Enabling takes the key, moves the serial and leaves the zone signing.</summary>
    [Fact]
    public void Enabling_takes_the_key_and_moves_the_serial()
    {
        var zone = NewZone();
        var before = zone.Serial;
        var key = DnssecKeyPolicy.Generate();

        var verdict = zone.EnableSigning(Guid.NewGuid(), key, Created.AddHours(1));

        Assert.Equal(DnssecTransitionVerdict.Accepted, verdict);
        Assert.Equal(DnssecState.Signing, zone.DnssecState);
        Assert.True(zone.Serial > before);
        var stored = Assert.Single(zone.SigningKeys);
        Assert.Equal(key.PublicKey, stored.PublicKey);
        Assert.Equal(key.PrivateKey.Reveal(), stored.PrivateKey);
        Assert.Equal((257, 13, key.KeyTag), (stored.Flags, stored.Algorithm, stored.KeyTag));
    }

    /// <summary>Enabling a zone that is not off is refused and changes nothing: no second key, no serial.</summary>
    [Fact]
    public void Enabling_a_zone_that_is_not_off_is_refused_and_changes_nothing()
    {
        var zone = NewZone();
        Sign(zone, Created);
        var serial = zone.Serial;
        var first = zone.SigningKeys.Single().Id;

        var verdict = zone.EnableSigning(Guid.NewGuid(), DnssecKeyPolicy.Generate(), Created.AddHours(2));

        Assert.Equal(DnssecTransitionVerdict.NotOff, verdict);
        Assert.Equal(serial, zone.Serial);
        Assert.Equal(first, Assert.Single(zone.SigningKeys).Id);
    }

    /// <summary>Beginning a withdrawal records its start, keeps the key and moves the serial.</summary>
    [Fact]
    public void Beginning_a_withdrawal_records_its_start_and_keeps_the_key()
    {
        var zone = NewZone();
        Sign(zone, Created);
        var serial = zone.Serial;
        var key = zone.SigningKeys.Single().Id;

        var verdict = zone.BeginWithdrawal(Created.AddDays(1));

        Assert.Equal(DnssecTransitionVerdict.Accepted, verdict);
        Assert.Equal(DnssecState.Withdrawing, zone.DnssecState);
        Assert.Equal(Created.AddDays(1), zone.DnssecWithdrawalStartedAt);
        Assert.True(zone.Serial > serial);
        Assert.Equal(key, Assert.Single(zone.SigningKeys).Id);
    }

    /// <summary>A withdrawal is refused on a zone that is off or already being withdrawn.</summary>
    [Fact]
    public void A_withdrawal_is_refused_on_a_zone_that_is_off_or_already_withdrawing()
    {
        var off = NewZone();
        var withdrawing = NewZone();
        Sign(withdrawing, Created);
        withdrawing.BeginWithdrawal(Created);
        var serial = withdrawing.Serial;

        Assert.Equal(DnssecTransitionVerdict.NotSigning, off.BeginWithdrawal(Created));
        Assert.Equal(DnssecTransitionVerdict.NotSigning, withdrawing.BeginWithdrawal(Created.AddDays(1)));
        Assert.Equal(Created, withdrawing.DnssecWithdrawalStartedAt);
        Assert.Equal(serial, withdrawing.Serial);
    }

    /// <summary>Planted failure: the end of a withdrawal is refused at every instant short of 48 hours, whoever asks.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(47)]
    public void The_end_of_a_withdrawal_is_refused_before_forty_eight_hours(int hours)
    {
        var zone = NewZone();
        Sign(zone, Created);
        zone.BeginWithdrawal(Created.AddDays(1));
        var serial = zone.Serial;

        var verdict = zone.FinishWithdrawal(Created.AddDays(1).AddHours(hours));

        Assert.Equal(DnssecTransitionVerdict.TooEarly, verdict);
        Assert.Equal(DnssecState.Withdrawing, zone.DnssecState);
        Assert.Single(zone.SigningKeys);
        Assert.Equal(serial, zone.Serial);
    }

    /// <summary>Inverse control: at 48 hours the zone is off, the key is gone and the serial moved.</summary>
    [Fact]
    public void At_forty_eight_hours_the_withdrawal_ends_and_the_key_is_gone()
    {
        var zone = NewZone();
        Sign(zone, Created);
        zone.BeginWithdrawal(Created.AddDays(1));
        var serial = zone.Serial;

        var verdict = zone.FinishWithdrawal(Created.AddDays(3));

        Assert.Equal(DnssecTransitionVerdict.Accepted, verdict);
        Assert.Equal(DnssecState.Off, zone.DnssecState);
        Assert.Empty(zone.SigningKeys);
        Assert.Null(zone.DnssecWithdrawalStartedAt);
        Assert.True(zone.Serial > serial);
    }

    /// <summary>A zone that is off or merely signing has no withdrawal to end, however late it is.</summary>
    [Fact]
    public void A_zone_that_is_not_withdrawing_has_nothing_to_end()
    {
        var off = NewZone();
        var signing = NewZone();
        Sign(signing, Created);

        Assert.Equal(DnssecTransitionVerdict.NotWithdrawing, off.FinishWithdrawal(Created.AddYears(1)));
        Assert.Equal(DnssecTransitionVerdict.NotWithdrawing, signing.FinishWithdrawal(Created.AddYears(1)));
        Assert.Single(signing.SigningKeys);
    }

    /// <summary>After the whole cycle the zone can be signed again, with a different key.</summary>
    [Fact]
    public void After_the_whole_cycle_the_zone_can_be_signed_again_with_a_new_key()
    {
        var zone = NewZone();
        Sign(zone, Created);
        var firstTag = zone.SigningKeys.Single().PublicKey;
        zone.BeginWithdrawal(Created);
        zone.FinishWithdrawal(Created.AddDays(2));

        var verdict = zone.EnableSigning(Guid.NewGuid(), DnssecKeyPolicy.Generate(), Created.AddDays(3));

        Assert.Equal(DnssecTransitionVerdict.Accepted, verdict);
        Assert.NotEqual(firstTag, zone.SigningKeys.Single().PublicKey);
    }

    /// <summary>A change of the zone's records while it is signed does not touch the key or the step.</summary>
    [Fact]
    public void Changing_records_while_signing_leaves_the_key_and_the_step_alone()
    {
        var zone = NewZone();
        Sign(zone, Created);
        var key = zone.SigningKeys.Single().Id;

        zone.AddRecord(
            Guid.NewGuid(),
            new RecordDraft("www", RecordType.A, 300, "203.0.113.7"),
            10,
            Created.AddHours(1));

        Assert.Equal(DnssecState.Signing, zone.DnssecState);
        Assert.Equal(key, Assert.Single(zone.SigningKeys).Id);
    }
}
