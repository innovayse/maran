using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain;

/// <summary>
/// The records a template creates are the zone's own to edit and remove, become the customer's when edited, and
/// arrive in one step that moves the serial once and honours the record cap.
/// </summary>
public sealed class ZoneTemplateRecordsTests
{
    /// <summary>The instant the zone is created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds an empty zone.</summary>
    /// <returns>The zone.</returns>
    private static Zone NewZone()
    {
        return new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.com", Created);
    }

    /// <summary>Builds an A record draft.</summary>
    /// <param name="name">The owner name.</param>
    /// <returns>The draft.</returns>
    private static RecordDraft A(string name)
    {
        return new RecordDraft(name, RecordType.A, 300, "203.0.113.7");
    }

    /// <summary>Template records arrive with the template origin and move the serial once for the whole set.</summary>
    [Fact]
    public void Template_records_arrive_with_their_origin_and_move_the_serial_once()
    {
        var zone = NewZone();

        var verdict = zone.AddTemplateRecords([A("@"), A("www"), A("mail")], 10, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        Assert.All(zone.Records, record =>
        {
            Assert.Equal(RecordOrigin.Template, record.Origin);
        });
        Assert.Equal(3, zone.Records.Count);
        Assert.Equal(2026110901, zone.Serial);
    }

    /// <summary>An empty expansion changes nothing, not even the serial.</summary>
    [Fact]
    public void An_empty_set_leaves_the_zone_untouched()
    {
        var zone = NewZone();
        var serial = zone.Serial;

        var verdict = zone.AddTemplateRecords([], 10, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        Assert.Equal(serial, zone.Serial);
    }

    /// <summary>A set larger than the cap is refused whole: no record of it is added.</summary>
    [Fact]
    public void A_set_over_the_cap_is_refused_whole()
    {
        var zone = NewZone();

        var verdict = zone.AddTemplateRecords([A("@"), A("www"), A("mail")], 2, Created);

        Assert.Equal(RecordRuleVerdict.ZoneFull, verdict);
        Assert.Empty(zone.Records);
    }

    /// <summary>The set is weighed like any other: a duplicate inside it refuses it whole.</summary>
    [Fact]
    public void A_duplicate_inside_the_set_refuses_it_whole()
    {
        var zone = NewZone();

        var verdict = zone.AddTemplateRecords([A("www"), A("WWW")], 10, Created);

        Assert.Equal(RecordRuleVerdict.Duplicate, verdict);
        Assert.Empty(zone.Records);
    }

    /// <summary>The owner may change a template record, and it is then the customer's.</summary>
    [Fact]
    public void Changing_a_template_record_makes_it_a_customer_record()
    {
        var zone = NewZone();
        zone.AddTemplateRecords([A("www")], 10, Created);
        var id = zone.Records.Single().Id;

        var verdict = zone.ChangeRecord(id, new RecordDraft("www", RecordType.A, 300, "203.0.113.99"), Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        Assert.Equal(RecordOrigin.Customer, zone.Records.Single().Origin);
        Assert.Equal("203.0.113.99", zone.Records.Single().Value);
    }

    /// <summary>The owner may remove a template record.</summary>
    [Fact]
    public void A_template_record_may_be_removed()
    {
        var zone = NewZone();
        zone.AddTemplateRecords([A("www")], 10, Created);

        var verdict = zone.RemoveRecord(zone.Records.Single().Id, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        Assert.Empty(zone.Records);
    }

    /// <summary>An import replaces the records the owner may edit, a template's among them.</summary>
    [Fact]
    public void An_import_replaces_template_records_too()
    {
        var zone = NewZone();
        zone.AddTemplateRecords([A("www")], 10, Created);

        var verdict = zone.ReplaceRecords([A("blog")], 10, Created);

        Assert.Equal(RecordRuleVerdict.Accepted, verdict);
        var only = Assert.Single(zone.Records);
        Assert.Equal("blog", only.Name);
        Assert.Equal(RecordOrigin.Customer, only.Origin);
    }
}
