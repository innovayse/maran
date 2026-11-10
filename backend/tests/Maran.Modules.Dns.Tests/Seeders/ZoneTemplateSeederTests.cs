using Maran.Modules.Dns.Commands.SaveZoneTemplate;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Dns.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Seeders;

/// <summary>
/// The built-in templates: written once and never twice, never over an administrator's edit, the default chosen
/// only while none is, and every record they hold passes the same rules an administrator's own would.
/// </summary>
public sealed class ZoneTemplateSeederTests
{
    /// <summary>Seeding an empty database writes both built-in templates and makes "web" the default.</summary>
    [Fact]
    public async Task Seeding_writes_both_built_in_templates_and_chooses_web()
    {
        using var context = new DnsTestContext();

        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        var templates = await context.Database.ZoneTemplates.AsNoTracking().Include(row => row.Records).ToListAsync();
        Assert.Equal(
            [ZoneTemplateSeeder.WebName, ZoneTemplateSeeder.WebAndMailName],
            templates.Select(template =>
            {
                return template.Name;
            }).Order(StringComparer.Ordinal));
        Assert.All(templates, template =>
        {
            Assert.True(template.IsBuiltIn);
        });
        Assert.Equal(3, templates.Single(template =>
        {
            return template.Id == ZoneTemplateSeeder.WebTemplateId;
        }).Records.Count);
        Assert.Equal(8, templates.Single(template =>
        {
            return template.Id == ZoneTemplateSeeder.WebAndMailTemplateId;
        }).Records.Count);
        var settings = await context.Database.DnsSettings.AsNoTracking().SingleAsync();
        Assert.Equal(ZoneTemplateSeeder.WebTemplateId, settings.DefaultTemplateId);
    }

    /// <summary>Seeding twice writes nothing the second time: no duplicate template, record or settings row.</summary>
    [Fact]
    public async Task Seeding_twice_changes_nothing_the_second_time()
    {
        using var context = new DnsTestContext();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        Assert.Equal(2, await context.Database.ZoneTemplates.CountAsync());
        Assert.Equal(11, await context.Database.ZoneTemplateRecords.CountAsync());
        Assert.Equal(1, await context.Database.DnsSettings.CountAsync());
    }

    /// <summary>A built-in template an administrator edited keeps the edit across a restart.</summary>
    [Fact]
    public async Task Seeding_never_overwrites_an_edited_built_in_template()
    {
        using var context = new DnsTestContext();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);
        var web = await context.Database.ZoneTemplates.Include(row => row.Records)
            .SingleAsync(row => row.Id == ZoneTemplateSeeder.WebTemplateId);
        web.Replace("web", "edited", [new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}")], context.Clock.UtcNow);
        await context.Database.SaveChangesAsync();

        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        var after = await context.Database.ZoneTemplates.AsNoTracking().Include(row => row.Records)
            .SingleAsync(row => row.Id == ZoneTemplateSeeder.WebTemplateId);
        Assert.Equal("edited", after.Description);
        Assert.Single(after.Records);
    }

    /// <summary>An administrator's choice of default survives a restart; an unset default is filled in.</summary>
    [Fact]
    public async Task Seeding_never_undoes_the_administrators_default()
    {
        using var context = new DnsTestContext();
        await context.EnableDnsServerAsync();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);
        var settings = await context.Database.DnsSettings.SingleAsync();
        settings.ChooseDefaultTemplate(ZoneTemplateSeeder.WebAndMailTemplateId, context.Clock.UtcNow);
        await context.Database.SaveChangesAsync();

        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        Assert.Equal(
            ZoneTemplateSeeder.WebAndMailTemplateId,
            (await context.Database.DnsSettings.AsNoTracking().SingleAsync()).DefaultTemplateId);
        Assert.True(settings.Enabled);
    }

    /// <summary>The seed leaves the stated settings of an existing row exactly as they were.</summary>
    [Fact]
    public async Task Seeding_keeps_the_stated_settings()
    {
        using var context = new DnsTestContext();
        await context.EnableDnsServerAsync();

        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        var settings = await context.Database.DnsSettings.AsNoTracking().SingleAsync();
        Assert.Equal("ns1.panel.test", settings.PrimaryNameserver);
        Assert.True(settings.Enabled);
        Assert.Equal(ZoneTemplateSeeder.WebTemplateId, settings.DefaultTemplateId);
    }

    /// <summary>A template an administrator already made under a built-in name stops that built-in from being written.</summary>
    [Fact]
    public async Task Seeding_does_not_collide_with_an_existing_name()
    {
        using var context = new DnsTestContext();
        await context.SaveTemplate.HandleAsync(
            new SaveZoneTemplateCommand("Web", string.Empty, []),
            CancellationToken.None);

        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        Assert.Equal(2, await context.Database.ZoneTemplates.CountAsync());
        Assert.DoesNotContain(context.Database.ZoneTemplates.AsNoTracking(), template =>
        {
            return template.Id == ZoneTemplateSeeder.WebTemplateId;
        });
    }

    /// <summary>Every record of both built-in templates passes the rules an administrator's own record would.</summary>
    [Fact]
    public void Every_built_in_record_passes_the_template_record_rules()
    {
        foreach (var record in ZoneTemplateSeeder.WebAndMailRecords)
        {
            Assert.True(TemplateRecordRules.Check(TemplateExpansionPolicy.Normalize(record)).IsAccepted, record.Name);
        }

        Assert.Equal(ZoneTemplateSeeder.WebRecords, ZoneTemplateSeeder.WebAndMailRecords.Take(3));
    }
}
