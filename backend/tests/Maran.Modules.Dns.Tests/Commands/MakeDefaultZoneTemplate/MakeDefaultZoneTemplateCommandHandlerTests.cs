using Maran.Modules.Dns.Commands.MakeDefaultZoneTemplate;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.MakeDefaultZoneTemplate;

/// <summary>Choosing the default template: it is recorded, journalled, and a missing template is not found.</summary>
public sealed class MakeDefaultZoneTemplateCommandHandlerTests
{
    /// <summary>Choosing a template records it in the settings and journals the change.</summary>
    [Fact]
    public async Task Making_a_template_the_default_records_it_and_journals_it()
    {
        using var context = new DnsTestContext();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        var result = await context.MakeDefaultTemplate.HandleAsync(
            new MakeDefaultZoneTemplateCommand(ZoneTemplateSeeder.WebAndMailTemplateId, "203.0.113.1", "tests"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var settings = await context.Database.DnsSettings.AsNoTracking().SingleAsync();
        Assert.Equal(ZoneTemplateSeeder.WebAndMailTemplateId, settings.DefaultTemplateId);
        var entry = Assert.Single(context.Entries);
        Assert.Equal(DnsAuditActions.ZoneTemplateDefaultChanged, entry.Action);
        Assert.Equal(ZoneTemplateSeeder.WebAndMailName, entry.Subject);
    }

    /// <summary>Choosing a template when the settings were never saved creates the row without stating anything else.</summary>
    [Fact]
    public async Task Making_a_default_before_any_settings_exist_creates_a_blank_row()
    {
        using var context = new DnsTestContext();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);
        context.Database.DnsSettings.RemoveRange(context.Database.DnsSettings);
        await context.Database.SaveChangesAsync();

        await context.MakeDefaultTemplate.HandleAsync(
            new MakeDefaultZoneTemplateCommand(ZoneTemplateSeeder.WebTemplateId, "203.0.113.1", "tests"),
            CancellationToken.None);

        var settings = await context.Database.DnsSettings.AsNoTracking().SingleAsync();
        Assert.False(settings.IsComplete);
        Assert.False(settings.Enabled);
        Assert.Equal(ZoneTemplateSeeder.WebTemplateId, settings.DefaultTemplateId);
    }

    /// <summary>A template that does not exist is not found, nothing is recorded, and the refusal is journalled.</summary>
    [Fact]
    public async Task Making_a_missing_template_the_default_is_not_found()
    {
        using var context = new DnsTestContext();
        await context.EnableDnsServerAsync();

        var result = await context.MakeDefaultTemplate.HandleAsync(
            new MakeDefaultZoneTemplateCommand(Guid.NewGuid(), "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("ZoneTemplateNotFound", result.Error!.Code);
        Assert.Null((await context.Database.DnsSettings.AsNoTracking().SingleAsync()).DefaultTemplateId);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }
}
