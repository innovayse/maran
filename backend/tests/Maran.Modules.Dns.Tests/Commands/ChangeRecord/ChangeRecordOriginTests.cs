using Maran.Modules.Dns.Commands.ChangeRecord;
using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Commands.DeleteRecord;
using Maran.Modules.Dns.Commands.ReplaceRecords;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Dns.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.ChangeRecord;

/// <summary>
/// Who may change or remove a record is decided by who wrote it: a record a template created is the owner's, one
/// the panel keeps (stored with the system origin) is not, and the refusal reaches neither the host nor the row.
/// </summary>
public sealed class ChangeRecordOriginTests
{
    /// <summary>The account that owns the zone.</summary>
    private static readonly Guid AccountId = new("44444444-4444-4444-8444-444444444444");

    /// <summary>Builds a world with a zone made from the web template and its apex record stored as the panel's own.</summary>
    /// <returns>The world, the zone's id, the panel-kept record's id and the template record's id.</returns>
    private static async Task<World> WorldAsync()
    {
        var context = new DnsTestContext();
        context.Options.Value.MaxRecordsPerZone = 50;
        context.Accounts.Add(AccountId, "dave", maxDnsZones: 5);
        await context.EnableDnsServerAsync();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);
        var created = await context.CreateZone.HandleAsync(
            new CreateZoneCommand(AccountId, "example.com", "203.0.113.1", "tests", ZoneTemplateSeeder.WebTemplateId),
            CancellationToken.None);
        var records = await context.Database.ZoneRecords.ToListAsync();
        var apex = records.Single(record =>
        {
            return record.Name == "@";
        });
        var www = records.Single(record =>
        {
            return record.Name == "www";
        });
        context.Database.Entry(apex).Property(record => record.Origin).CurrentValue = RecordOrigin.System;
        await context.Database.SaveChangesAsync();

        return new World(context, created.Value.Id, apex.Id, www.Id);
    }

    /// <summary>The panel's own record cannot be changed or removed, and the host is not told.</summary>
    [Fact]
    public async Task A_system_record_is_refused_for_change_and_removal()
    {
        var world = await WorldAsync();
        using var context = world.Context;
        var (zoneId, systemId) = (world.ZoneId, world.SystemRecordId);
        var puts = context.Agent.Puts.Count;

        var changed = await context.ChangeRecord.HandleAsync(
            new ChangeRecordCommand(new RecordDraft("@", RecordType.A, 300, "198.51.100.9"), zoneId, systemId, "203.0.113.1", "tests"),
            CancellationToken.None);
        var removed = await context.DeleteRecord.HandleAsync(
            new DeleteRecordCommand(zoneId, systemId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("RecordNotEditable", changed.Error!.Code);
        Assert.Equal("RecordNotEditable", removed.Error!.Code);
        Assert.Equal(puts, context.Agent.Puts.Count);
        Assert.Contains(await context.Database.ZoneRecords.AsNoTracking().ToListAsync(), record =>
        {
            return record.Id == systemId && record.Value == "203.0.113.5";
        });
    }

    /// <summary>Inverse control: the template's other record is the owner's, and removing it works.</summary>
    [Fact]
    public async Task A_template_record_is_removable_by_its_owner()
    {
        var world = await WorldAsync();
        using var context = world.Context;
        var (zoneId, templateId) = (world.ZoneId, world.TemplateRecordId);

        var removed = await context.DeleteRecord.HandleAsync(
            new DeleteRecordCommand(zoneId, templateId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Assert.DoesNotContain(await context.Database.ZoneRecords.AsNoTracking().ToListAsync(), record =>
        {
            return record.Id == templateId;
        });
    }

    /// <summary>An import replaces the template's record and keeps the panel's own.</summary>
    [Fact]
    public async Task An_import_keeps_the_system_record_and_replaces_the_template_one()
    {
        var world = await WorldAsync();
        using var context = world.Context;
        var (zoneId, systemId) = (world.ZoneId, world.SystemRecordId);

        var replaced = await context.ReplaceRecords.HandleAsync(
            new ReplaceRecordsCommand([new RecordDraft("blog", RecordType.A, 300, "198.51.100.9")], zoneId, "203.0.113.1", "tests"),
            CancellationToken.None);

        Assert.True(replaced.IsSuccess);
        var stored = await context.Database.ZoneRecords.AsNoTracking().ToListAsync();
        Assert.Equal(["@", "blog"], stored.Select(record =>
        {
            return record.Name;
        }).Order(StringComparer.Ordinal));
        Assert.Equal(RecordOrigin.System, stored.Single(record =>
        {
            return record.Id == systemId;
        }).Origin);
    }

    /// <summary>One test's world: the context and the identities the tests aim at.</summary>
    /// <param name="Context">The world.</param>
    /// <param name="ZoneId">The zone made from the template.</param>
    /// <param name="SystemRecordId">The apex record, stored as the panel's own.</param>
    /// <param name="TemplateRecordId">The www record, still the template's.</param>
    private sealed record World(DnsTestContext Context, Guid ZoneId, Guid SystemRecordId, Guid TemplateRecordId);
}
