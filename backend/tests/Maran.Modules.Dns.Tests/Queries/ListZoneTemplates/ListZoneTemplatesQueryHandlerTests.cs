using Maran.Modules.Dns.Commands.SaveZoneTemplate;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Queries.GetZoneTemplate;
using Maran.Modules.Dns.Queries.ListZoneTemplates;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Dns.Tests.Queries.ListZoneTemplates;

/// <summary>The administrator's template list and detail: counts, the default flag, order, and a missing template.</summary>
public sealed class ListZoneTemplatesQueryHandlerTests
{
    /// <summary>The list shows each template with its record count and the default flag, by name.</summary>
    [Fact]
    public async Task The_list_shows_counts_and_the_default_by_name()
    {
        using var context = new DnsTestContext();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        var result = await context.ListTemplates.HandleAsync(new ListZoneTemplatesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Total);
        Assert.Equal(["web", "web and mail"], result.Value.Items.Select(row =>
        {
            return row.Name;
        }));
        Assert.Equal([3, 8], result.Value.Items.Select(row =>
        {
            return row.RecordCount;
        }));
        Assert.Equal([true, false], result.Value.Items.Select(row =>
        {
            return row.IsDefault;
        }));
        Assert.All(result.Value.Items, row =>
        {
            Assert.True(row.IsBuiltIn);
        });
    }

    /// <summary>A descending sort by name reverses the order.</summary>
    [Fact]
    public async Task A_descending_sort_reverses_the_order()
    {
        using var context = new DnsTestContext();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        var result = await context.ListTemplates.HandleAsync(
            new ListZoneTemplatesQuery(Sort: SortRequest.Parse("-name")), CancellationToken.None);

        Assert.Equal(["web and mail", "web"], result.Value.Items.Select(row =>
        {
            return row.Name;
        }));
    }

    /// <summary>The detail carries the records in order; a missing template is not found.</summary>
    [Fact]
    public async Task The_detail_carries_the_records_and_a_missing_template_is_not_found()
    {
        using var context = new DnsTestContext();
        var saved = await context.SaveTemplate.HandleAsync(
            new SaveZoneTemplateCommand(
                "Mine",
                "about",
                [
                    new TemplateRecordDraft("www", RecordType.A, 600, "{ipv4}"),
                    new TemplateRecordDraft("@", RecordType.Mx, null, "mail.{apex}", Priority: 10),
                ]),
            CancellationToken.None);

        var found = await context.GetTemplate.HandleAsync(new GetZoneTemplateQuery(saved.Value.Id), CancellationToken.None);
        var missing = await context.GetTemplate.HandleAsync(new GetZoneTemplateQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(["www", "@"], found.Value.Records.Select(record =>
        {
            return record.Name;
        }));
        Assert.Equal(600, found.Value.Records[0].Ttl);
        Assert.Null(found.Value.Records[1].Ttl);
        Assert.Equal(10, found.Value.Records[1].Priority);
        Assert.Equal("ZoneTemplateNotFound", missing.Error!.Code);
        Assert.False(found.Value.IsDefault);
    }
}
