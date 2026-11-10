using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain;

/// <summary>A template is the gate of its own content: its name key follows its name and its records keep their order.</summary>
public sealed class ZoneTemplateTests
{
    /// <summary>The instant templates are created.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A template cannot be created from a name or description the policy refuses.</summary>
    [Fact]
    public void A_template_cannot_be_created_with_a_refused_name()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new ZoneTemplate(Guid.NewGuid(), string.Empty, string.Empty, false, Now);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return new ZoneTemplate(Guid.NewGuid(), "ok", "line\nbreak", false, Now);
        });
    }

    /// <summary>Replacing the template renames it, moves its comparison key, and keeps the records in written order.</summary>
    [Fact]
    public void Replacing_renames_the_template_and_keeps_the_records_in_order()
    {
        var template = new ZoneTemplate(Guid.NewGuid(), "Old", string.Empty, false, Now);

        template.Replace(
            "New Name",
            "about",
            [
                new TemplateRecordDraft("www", RecordType.A, null, "{ipv4}"),
                new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}"),
            ],
            Now.AddMinutes(1));

        Assert.Equal("New Name", template.Name);
        Assert.Equal("new name", template.NameKey);
        Assert.Equal(["www", "@"], template.Records.Select(record =>
        {
            return record.Name;
        }));
        Assert.Equal(Now.AddMinutes(1), template.UpdatedAt);
    }

    /// <summary>A second replacement drops the first one's records.</summary>
    [Fact]
    public void A_second_replacement_drops_the_first_records()
    {
        var template = new ZoneTemplate(Guid.NewGuid(), "t", string.Empty, false, Now);
        template.Replace("t", string.Empty, [new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}")], Now);

        template.Replace("t", string.Empty, [], Now);

        Assert.Empty(template.Records);
    }
}
