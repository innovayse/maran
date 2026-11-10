using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain;

/// <summary>
/// The zone's three challenge methods: what a challenge record is, that it is outside the record cap but
/// bounded to two values per name, that it can be removed through its own method only, and that a customer's
/// record at the same name is never touched. Each refusal sits beside the acceptance that proves the method
/// can say yes.
/// </summary>
public sealed class ZoneChallengeRecordTests
{
    /// <summary>The instant the zone is created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The most customer records the zone may hold in these tests.</summary>
    private const int Limit = 2;

    /// <summary>Builds an empty zone.</summary>
    /// <returns>The zone.</returns>
    private static Zone NewZone()
    {
        return new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.com", Created);
    }

    /// <summary>Builds a challenge draft at a name with a value made of one repeated character.</summary>
    /// <param name="fill">The character the forty-three characters repeat.</param>
    /// <param name="name">The owner name.</param>
    /// <returns>The draft.</returns>
    private static RecordDraft Challenge(char fill, string name = ChallengeRecordPolicy.Label)
    {
        return new RecordDraft(name, RecordType.Txt, DnsLimits.MinTtl, new string(fill, 43));
    }

    /// <summary>A challenge record is added with its own origin and moves the serial.</summary>
    [Fact]
    public void A_challenge_record_is_added_with_its_own_origin_and_moves_the_serial()
    {
        var zone = NewZone();
        var before = zone.Serial;

        var verdict = zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        var record = Assert.Single(zone.Records);
        Assert.Equal(RecordOrigin.AcmeChallenge, record.Origin);
        Assert.True(zone.Serial > before);
    }

    /// <summary>A zone full of customer records still takes a challenge record, which the customer's own add does not.</summary>
    [Fact]
    public void A_zone_full_of_customer_records_still_takes_a_challenge_record()
    {
        var zone = NewZone();
        zone.AddRecord(Guid.NewGuid(), new RecordDraft("a", RecordType.A, 300, "203.0.113.1"), Limit, Created);
        zone.AddRecord(Guid.NewGuid(), new RecordDraft("b", RecordType.A, 300, "203.0.113.2"), Limit, Created);

        // The accepting control: the cap is real, so the exemption below is not an accident of an empty zone.
        var customer = zone.AddRecord(Guid.NewGuid(), new RecordDraft("c", RecordType.A, 300, "203.0.113.3"), Limit, Created);
        var challenge = zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), Created);

        Assert.Equal(RecordRuleVerdict.ZoneFull, customer);
        Assert.Equal(RecordRuleVerdict.Accepted, challenge);
    }

    /// <summary>Two values may sit at one name and a third is refused.</summary>
    [Fact]
    public void Two_values_may_sit_at_one_name_and_a_third_is_refused()
    {
        var zone = NewZone();

        var first = zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), Created);
        var second = zone.AddChallengeRecord(Guid.NewGuid(), Challenge('b'), Created);
        var third = zone.AddChallengeRecord(Guid.NewGuid(), Challenge('c'), Created);

        Assert.Equal(RecordRuleVerdict.Accepted, first);
        Assert.Equal(RecordRuleVerdict.Accepted, second);
        Assert.Equal(RecordRuleVerdict.ChallengeValuesFull, third);
        Assert.Equal(2, zone.Records.Count);
    }

    /// <summary>The bound is per name: a second name takes its own two values.</summary>
    [Fact]
    public void The_bound_is_per_name()
    {
        var zone = NewZone();
        zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), Created);
        zone.AddChallengeRecord(Guid.NewGuid(), Challenge('b'), Created);

        var elsewhere = zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a', "_acme-challenge.www"), Created);

        Assert.Equal(RecordRuleVerdict.Accepted, elsewhere);
    }

    /// <summary>The same value twice at one name is a duplicate.</summary>
    [Fact]
    public void The_same_value_twice_at_one_name_is_a_duplicate()
    {
        var zone = NewZone();
        zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), Created);

        var again = zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), Created);

        Assert.Equal(RecordRuleVerdict.Duplicate, again);
    }

    /// <summary>Anything but a TXT at a challenge name holding a digest is a bug in the caller and is refused loudly.</summary>
    [Fact]
    public void Anything_but_a_digest_at_a_challenge_name_is_refused_loudly()
    {
        var zone = NewZone();

        Assert.Throws<ArgumentException>(() =>
        {
            return zone.AddChallengeRecord(Guid.NewGuid(), new RecordDraft("www", RecordType.Txt, 60, new string('a', 43)), Created);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return zone.AddChallengeRecord(Guid.NewGuid(), new RecordDraft("_acme-challenge", RecordType.Txt, 60, "v=spf1 -all"), Created);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return zone.AddChallengeRecord(Guid.NewGuid(), new RecordDraft("_acme-challenge", RecordType.A, 60, "203.0.113.9"), Created);
        });
        Assert.Empty(zone.Records);
    }

    /// <summary>A challenge record is removed by its own method, and the serial moves.</summary>
    [Fact]
    public void A_challenge_record_is_removed_by_its_own_method()
    {
        var zone = NewZone();
        var id = Guid.NewGuid();
        zone.AddChallengeRecord(id, Challenge('a'), Created);
        var before = zone.Serial;

        var verdict = zone.RemoveChallengeRecord(id, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        Assert.Empty(zone.Records);
        Assert.True(zone.Serial > before);
    }

    /// <summary>The challenge method never removes a customer's record, however it is asked.</summary>
    [Fact]
    public void The_challenge_removal_never_removes_a_customer_record()
    {
        var zone = NewZone();
        var customerId = Guid.NewGuid();
        zone.AddRecord(customerId, new RecordDraft("_acme-challenge", RecordType.Txt, 300, "customer text"), Limit, Created);

        var verdict = zone.RemoveChallengeRecord(customerId, Created);

        Assert.Equal(RecordRuleVerdict.RecordNotEditable, verdict);
        Assert.Single(zone.Records);
    }

    /// <summary>Removing a record the zone does not hold is reported, not thrown.</summary>
    [Fact]
    public void Removing_a_record_the_zone_does_not_hold_is_reported()
    {
        var zone = NewZone();

        Assert.Equal(RecordRuleVerdict.RecordNotFound, zone.RemoveChallengeRecord(Guid.NewGuid(), Created));
    }

    /// <summary>A customer cannot change or remove a challenge record through the customer methods, and can their own.</summary>
    [Fact]
    public void A_customer_cannot_change_or_remove_a_challenge_record()
    {
        var zone = NewZone();
        var challengeId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        zone.AddChallengeRecord(challengeId, Challenge('a'), Created);
        zone.AddRecord(customerId, new RecordDraft("www", RecordType.A, 300, "203.0.113.1"), Limit, Created);

        var changed = zone.ChangeRecord(challengeId, Challenge('b'), Created);
        var removed = zone.RemoveRecord(challengeId, Created);

        Assert.Equal(RecordRuleVerdict.RecordNotEditable, changed);
        Assert.Equal(RecordRuleVerdict.RecordNotEditable, removed);

        // The accepting control: the same methods do edit and remove a customer's own record.
        var ownChanged = zone.ChangeRecord(customerId, new RecordDraft("www", RecordType.A, 300, "203.0.113.2"), Created);
        var ownRemoved = zone.RemoveRecord(customerId, Created);
        Assert.Equal(RecordRuleVerdict.Accepted, ownChanged);
        Assert.Equal(RecordRuleVerdict.Accepted, ownRemoved);
    }

    /// <summary>The sweep removes an old challenge record and keeps a fresh one and an old customer record at the same name.</summary>
    [Fact]
    public void The_sweep_removes_an_old_challenge_record_and_nothing_else()
    {
        var zone = NewZone();
        var oldChallenge = Created.AddHours(-3);
        var cutoff = Created.AddHours(-1);
        zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), oldChallenge);
        zone.AddChallengeRecord(Guid.NewGuid(), Challenge('b'), Created);
        zone.AddRecord(Guid.NewGuid(), new RecordDraft("_acme-challenge", RecordType.Txt, 300, "customer text"), Limit, oldChallenge);

        var removed = zone.RemoveStaleChallengeRecords(cutoff, Created);

        Assert.Equal(1, removed);
        Assert.Equal(2, zone.Records.Count);
        Assert.Contains(zone.Records, record =>
        {
            return record.Origin == RecordOrigin.Customer;
        });
        Assert.Contains(zone.Records, record =>
        {
            return record.Origin == RecordOrigin.AcmeChallenge && record.Value == new string('b', 43);
        });
    }

    /// <summary>A sweep that finds nothing leaves the serial alone.</summary>
    [Fact]
    public void A_sweep_that_finds_nothing_leaves_the_serial_alone()
    {
        var zone = NewZone();
        zone.AddChallengeRecord(Guid.NewGuid(), Challenge('a'), Created);
        var before = zone.Serial;

        var removed = zone.RemoveStaleChallengeRecords(Created.AddHours(-1), Created);

        Assert.Equal(0, removed);
        Assert.Equal(before, zone.Serial);
    }
}
