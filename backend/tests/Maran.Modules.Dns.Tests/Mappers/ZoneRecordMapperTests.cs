using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Tests.TestSupport;

namespace Maran.Modules.Dns.Tests.Mappers;

/// <summary>A record is shown as editable exactly when the owner may edit it, which a template record is.</summary>
public sealed class ZoneRecordMapperTests
{
    /// <summary>The instant the zone is created.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A template record and a customer record are editable; the origin is named for the viewer.</summary>
    [Fact]
    public void Template_and_customer_records_are_shown_as_editable()
    {
        var zone = new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.com", Now);
        zone.AddTemplateRecords([new RecordDraft("www", RecordType.A, 300, "203.0.113.7")], 10, Now);
        zone.AddRecord(Guid.NewGuid(), new RecordDraft("blog", RecordType.A, 300, "203.0.113.8"), 10, Now);

        var shown = zone.Records.Select(record =>
        {
            return ZoneRecordMapper.From(record, new KeyEchoLocalizer());
        }).ToList();

        Assert.All(shown, dto =>
        {
            Assert.True(dto.Editable);
        });
        Assert.Equal("DnsOriginTemplate", shown.Single(dto =>
        {
            return dto.Name == "www";
        }).OriginName);
        Assert.Equal(RecordOrigin.Template, shown.Single(dto =>
        {
            return dto.Name == "www";
        }).Origin);
    }
}
