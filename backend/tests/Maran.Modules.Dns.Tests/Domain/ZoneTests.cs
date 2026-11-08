using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain;

/// <summary>The zone is the gate of its own content: every change is a method, and every method moves the serial.</summary>
public sealed class ZoneTests
{
    /// <summary>The instant the zone is created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The most records the zone may hold in these tests.</summary>
    private const int Limit = 3;

    /// <summary>Builds an empty zone.</summary>
    /// <returns>The zone.</returns>
    private static Zone NewZone()
    {
        return new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.com", Created);
    }

    /// <summary>Builds an A record draft.</summary>
    /// <param name="name">The owner name.</param>
    /// <param name="address">The address.</param>
    /// <returns>The draft.</returns>
    private static RecordDraft A(string name, string address = "203.0.113.7")
    {
        return new RecordDraft(name, RecordType.A, 300, address);
    }

    /// <summary>A new zone starts at the first serial of its day with no records.</summary>
    [Fact]
    public void A_new_zone_starts_at_the_first_serial_of_its_day()
    {
        var zone = NewZone();

        Assert.Equal(2026110700, zone.Serial);
        Assert.Empty(zone.Records);
    }

    /// <summary>A name that is not an apex cannot become a zone.</summary>
    [Fact]
    public void A_name_that_is_not_an_apex_cannot_become_a_zone()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new Zone(Guid.NewGuid(), Guid.NewGuid(), "com", Created);
        });
    }

    /// <summary>Adding a record keeps it in its stored spelling and moves the serial.</summary>
    [Fact]
    public void Adding_a_record_moves_the_serial()
    {
        var zone = NewZone();

        var verdict = zone.AddRecord(Guid.NewGuid(), A(" WWW "), Limit, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        Assert.Equal("www", Assert.Single(zone.Records).Name);
        Assert.Equal(2026110701, zone.Serial);
    }

    /// <summary>A refused change leaves the zone and its serial untouched.</summary>
    [Fact]
    public void A_refused_change_leaves_the_zone_untouched()
    {
        var zone = NewZone();
        zone.AddRecord(Guid.NewGuid(), A("www"), Limit, Created);
        var serial = zone.Serial;

        var verdict = zone.AddRecord(Guid.NewGuid(), A("www"), Limit, Created);

        Assert.Equal(RecordRuleVerdict.Duplicate, verdict);
        Assert.Single(zone.Records);
        Assert.Equal(serial, zone.Serial);
    }

    /// <summary>A CNAME cannot share a name with other data, in either order, or sit at the apex.</summary>
    [Fact]
    public void A_cname_cannot_share_a_name_or_sit_at_the_apex()
    {
        var zone = NewZone();
        zone.AddRecord(Guid.NewGuid(), A("www"), Limit, Created);

        Assert.Equal(
            RecordRuleVerdict.CnameBesideOtherData,
            zone.AddRecord(Guid.NewGuid(), new RecordDraft("www", RecordType.Cname, 300, "a.example.net"), Limit, Created));
        Assert.Equal(
            RecordRuleVerdict.CnameAtApex,
            zone.AddRecord(Guid.NewGuid(), new RecordDraft("@", RecordType.Cname, 300, "a.example.net"), Limit, Created));

        zone.AddRecord(Guid.NewGuid(), new RecordDraft("blog", RecordType.Cname, 300, "a.example.net"), Limit, Created);

        Assert.Equal(
            RecordRuleVerdict.OtherDataBesideCname,
            zone.AddRecord(Guid.NewGuid(), A("blog"), Limit, Created));
    }

    /// <summary>The apex name servers are the panel's, so a customer cannot add one.</summary>
    [Fact]
    public void An_apex_name_server_cannot_be_added()
    {
        var zone = NewZone();

        var verdict = zone.AddRecord(Guid.NewGuid(), new RecordDraft("@", RecordType.Ns, 300, "ns.other.net"), Limit, Created);

        Assert.Equal(RecordRuleVerdict.NsAtApex, verdict);
    }

    /// <summary>A zone holding the most records refuses one more.</summary>
    [Fact]
    public void A_full_zone_refuses_one_more()
    {
        var zone = NewZone();
        for (var index = 0; index < Limit; index++)
        {
            zone.AddRecord(Guid.NewGuid(), A($"h{index}"), Limit, Created);
        }

        Assert.Equal(RecordRuleVerdict.ZoneFull, zone.AddRecord(Guid.NewGuid(), A("extra"), Limit, Created));
    }

    /// <summary>Changing and removing act on the named record and move the serial.</summary>
    [Fact]
    public void Changing_and_removing_act_on_the_named_record()
    {
        var zone = NewZone();
        var id = Guid.NewGuid();
        zone.AddRecord(id, A("www"), Limit, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, zone.ChangeRecord(id, A("www", "203.0.113.9"), Created));
        Assert.Equal("203.0.113.9", Assert.Single(zone.Records).Value);
        Assert.Equal(RecordRuleVerdict.RecordNotFound, zone.ChangeRecord(Guid.NewGuid(), A("x"), Created));

        Assert.Equal(RecordRuleVerdict.Accepted, zone.RemoveRecord(id, Created));
        Assert.Empty(zone.Records);
        Assert.Equal(RecordRuleVerdict.RecordNotFound, zone.RemoveRecord(id, Created));
        Assert.Equal(2026110703, zone.Serial);
    }

    /// <summary>A record can be changed to what it already says apart from its time to live, without clashing with itself.</summary>
    [Fact]
    public void A_record_does_not_clash_with_itself()
    {
        var zone = NewZone();
        var id = Guid.NewGuid();
        zone.AddRecord(id, A("www"), Limit, Created);

        var verdict = zone.ChangeRecord(id, new RecordDraft("www", RecordType.A, 600, "203.0.113.7"), Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        Assert.Equal(600, Assert.Single(zone.Records).Ttl);
    }

    /// <summary>An import swaps the whole set in, or changes nothing at all.</summary>
    [Fact]
    public void An_import_swaps_the_whole_set_or_changes_nothing()
    {
        var zone = NewZone();
        zone.AddRecord(Guid.NewGuid(), A("old"), Limit, Created);
        var serial = zone.Serial;

        var refused = zone.ReplaceRecords([A("a"), new RecordDraft("a", RecordType.Cname, 300, "x.example.net")], Limit, Created);

        Assert.Equal(RecordRuleVerdict.CnameBesideOtherData, refused);
        Assert.Equal("old", Assert.Single(zone.Records).Name);
        Assert.Equal(serial, zone.Serial);

        var accepted = zone.ReplaceRecords([A("a"), A("b")], Limit, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, accepted);
        Assert.Equal(["a", "b"], zone.Records.Select(record =>
        {
            return record.Name;
        }).Order().ToArray());
        Assert.True(zone.Serial > serial);
    }

    /// <summary>An import larger than the limit is refused whole.</summary>
    [Fact]
    public void An_import_larger_than_the_limit_is_refused()
    {
        var zone = NewZone();

        var verdict = zone.ReplaceRecords([A("a"), A("b"), A("c"), A("d")], Limit, Created);

        Assert.Equal(RecordRuleVerdict.ZoneFull, verdict);
        Assert.Empty(zone.Records);
    }

    /// <summary>A record that the shape policy refuses is a bug in the caller, and the zone says so loudly.</summary>
    [Fact]
    public void A_malformed_record_is_a_bug_and_throws()
    {
        var zone = NewZone();

        Assert.Throws<ArgumentException>(() =>
        {
            return zone.AddRecord(Guid.NewGuid(), A("www", "not-an-address"), Limit, Created);
        });
    }

    /// <summary>Reissuing moves the serial past the one the daemon serves without touching a record.</summary>
    [Fact]
    public void Reissuing_moves_the_serial_past_the_served_one()
    {
        var zone = NewZone();

        zone.Reissue(2026110750, Created);

        Assert.Equal(2026110751, zone.Serial);
    }
}
