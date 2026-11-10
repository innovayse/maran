using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.ListZoneTemplateChoices;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Dns.Tests.TestSupport;

namespace Maran.Modules.Dns.Tests.Queries.ListZoneTemplateChoices;

/// <summary>
/// What the zone-creation form learns about templates: an id, a name and the default flag, for a customer as for an
/// administrator, and never a record.
/// </summary>
public sealed class ListZoneTemplateChoicesQueryHandlerTests
{
    /// <summary>The shape of a choice has no member that could carry a template's records.</summary>
    [Fact]
    public void A_choice_carries_only_an_id_a_name_and_the_default_flag()
    {
        var members = typeof(ZoneTemplateChoiceDto).GetProperties()
            .Select(property =>
            {
                return property.Name;
            })
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Id", "IsDefault", "Name"], members);
    }

    /// <summary>Control for the census above: the detail shape of the same template does carry records.</summary>
    [Fact]
    public void The_detail_shape_does_carry_records()
    {
        Assert.NotNull(typeof(ZoneTemplateDetailDto).GetProperty("Records"));
    }

    /// <summary>A customer is offered every template, the default first.</summary>
    [Fact]
    public async Task A_customer_is_offered_every_template_with_the_default_first()
    {
        using var admin = new DnsTestContext();
        await admin.TemplateSeeder.SeedAsync(CancellationToken.None);
        using var customer = new DnsTestContext(new CustomerCurrentUser(Guid.NewGuid()), admin.DatabaseName);

        var result = await customer.ListTemplateChoices.HandleAsync(new ListZoneTemplateChoicesQuery(), CancellationToken.None);

        Assert.Equal(["web", "web and mail"], result.Value.Select(choice =>
        {
            return choice.Name;
        }));
        Assert.True(result.Value[0].IsDefault);
        Assert.False(result.Value[1].IsDefault);
        Assert.Equal(ZoneTemplateSeeder.WebTemplateId, result.Value[0].Id);
    }

    /// <summary>With no template and no settings the form is offered nothing, without failing.</summary>
    [Fact]
    public async Task With_no_templates_the_form_is_offered_nothing()
    {
        using var context = new DnsTestContext();

        var result = await context.ListTemplateChoices.HandleAsync(new ListZoneTemplateChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
