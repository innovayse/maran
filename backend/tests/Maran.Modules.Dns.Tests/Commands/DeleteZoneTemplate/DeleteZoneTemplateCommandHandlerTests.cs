using Maran.Modules.Dns.Commands.DeleteZoneTemplate;
using Maran.Modules.Dns.Commands.MakeDefaultZoneTemplate;
using Maran.Modules.Dns.Commands.SaveZoneTemplate;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.DeleteZoneTemplate;

/// <summary>A built-in template and the default are never deleted; a custom template is, with its records.</summary>
public sealed class DeleteZoneTemplateCommandHandlerTests
{
    /// <summary>Creates a custom template and returns its id.</summary>
    /// <param name="context">The world.</param>
    /// <param name="name">The template's name.</param>
    /// <returns>The id.</returns>
    private static async Task<Guid> CustomAsync(DnsTestContext context, string name)
    {
        var saved = await context.SaveTemplate.HandleAsync(
            new SaveZoneTemplateCommand(
                name, string.Empty, [new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}")]),
            CancellationToken.None);

        return saved.Value.Id;
    }

    /// <summary>A built-in template is refused with its own code and stays.</summary>
    [Fact]
    public async Task Deleting_a_built_in_template_is_refused()
    {
        using var context = new DnsTestContext();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        var result = await context.DeleteTemplate.HandleAsync(
            new DeleteZoneTemplateCommand(ZoneTemplateSeeder.WebAndMailTemplateId, "203.0.113.1", "tests"),
            CancellationToken.None);

        Assert.Equal("ZoneTemplateBuiltIn", result.Error!.Code);
        Assert.Equal(2, await context.Database.ZoneTemplates.CountAsync());
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Inverse control: a custom template that is not the default is deleted with its records.</summary>
    [Fact]
    public async Task Deleting_a_custom_template_removes_it_and_its_records()
    {
        using var context = new DnsTestContext();
        var id = await CustomAsync(context, "Mine");

        var result = await context.DeleteTemplate.HandleAsync(
            new DeleteZoneTemplateCommand(id, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.Database.ZoneTemplates.AsNoTracking());
        Assert.Empty(context.Database.ZoneTemplateRecords.AsNoTracking());
        var entry = context.Entries[^1];
        Assert.Equal(DnsAuditActions.ZoneTemplateDeleted, entry.Action);
        Assert.Equal("Mine", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The default template is refused until another is made the default.</summary>
    [Fact]
    public async Task Deleting_the_default_template_is_refused_until_another_is_chosen()
    {
        using var context = new DnsTestContext();
        var id = await CustomAsync(context, "Mine");
        await context.MakeDefaultTemplate.HandleAsync(
            new MakeDefaultZoneTemplateCommand(id, "203.0.113.1", "tests"), CancellationToken.None);
        var other = await CustomAsync(context, "Other");

        var refused = await context.DeleteTemplate.HandleAsync(
            new DeleteZoneTemplateCommand(id, "203.0.113.1", "tests"), CancellationToken.None);
        await context.MakeDefaultTemplate.HandleAsync(
            new MakeDefaultZoneTemplateCommand(other, "203.0.113.1", "tests"), CancellationToken.None);
        var allowed = await context.DeleteTemplate.HandleAsync(
            new DeleteZoneTemplateCommand(id, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("ZoneTemplateIsDefault", refused.Error!.Code);
        Assert.True(allowed.IsSuccess);
    }

    /// <summary>A template that does not exist is not found.</summary>
    [Fact]
    public async Task Deleting_a_missing_template_is_not_found()
    {
        using var context = new DnsTestContext();

        var result = await context.DeleteTemplate.HandleAsync(
            new DeleteZoneTemplateCommand(Guid.NewGuid(), "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("ZoneTemplateNotFound", result.Error!.Code);
    }
}
