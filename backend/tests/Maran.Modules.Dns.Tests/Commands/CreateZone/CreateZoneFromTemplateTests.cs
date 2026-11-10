using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Commands.ChangeRecord;
using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Commands.SaveZoneTemplate;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.CreateZone;

/// <summary>
/// Creating a zone from a template: the records travel in the FIRST publication, arrive with the template origin,
/// a record whose setting is not stated is skipped and reported, the cap still applies, and a template that
/// cannot be applied costs nothing. Each refusal has an accepting control.
/// </summary>
public sealed class CreateZoneFromTemplateTests
{
    /// <summary>The account the zones are created for.</summary>
    private static readonly Guid AccountId = new("33333333-3333-4333-8333-333333333333");

    /// <summary>Builds a world for an administrator with the server on, the built-in templates and a roomy cap.</summary>
    /// <returns>The world.</returns>
    private static async Task<DnsTestContext> WorldAsync()
    {
        var context = new DnsTestContext();
        context.Options.Value.MaxRecordsPerZone = 50;
        context.Accounts.Add(AccountId, "carol", maxDnsZones: 5);
        await context.EnableDnsServerAsync();
        await context.TemplateSeeder.SeedAsync(CancellationToken.None);

        return context;
    }

    /// <summary>Builds a create command naming a template.</summary>
    /// <param name="name">The apex.</param>
    /// <param name="templateId">The template, or null.</param>
    /// <returns>The command.</returns>
    private static CreateZoneCommand Create(string name, Guid? templateId)
    {
        return new CreateZoneCommand(AccountId, name, "203.0.113.1", "tests", templateId);
    }

    /// <summary>The records of the template are in the very first publication, with the template origin and one serial move.</summary>
    [Fact]
    public async Task The_first_publication_carries_the_template_records()
    {
        using var context = await WorldAsync();

        var result = await context.CreateZone.HandleAsync(
            Create("shop.example", ZoneTemplateSeeder.WebTemplateId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(context.Agent.Puts);
        Assert.Equal(["@", "www"], sent.Records.Select(record =>
        {
            return record.Name;
        }).Distinct());
        Assert.Equal("203.0.113.5", sent.Records.Single(record =>
        {
            return record.Name == "@";
        }).Value);
        Assert.Equal("shop.example", sent.Records.Single(record =>
        {
            return record.Name == "www";
        }).Value);
        var stored = await context.Database.ZoneRecords.AsNoTracking().ToListAsync();
        Assert.All(stored, record =>
        {
            Assert.Equal(RecordOrigin.Template, record.Origin);
        });
        Assert.Equal(2, result.Value.RecordCount);
        Assert.Equal(sent.Serial, (await context.Database.Zones.AsNoTracking().SingleAsync()).Serial);
    }

    /// <summary>With no IPv6 address in the settings the AAAA records are skipped, reported, and not invented.</summary>
    [Fact]
    public async Task A_missing_setting_skips_its_records_and_reports_them()
    {
        using var context = await WorldAsync();

        var result = await context.CreateZone.HandleAsync(
            Create("shop.example", ZoneTemplateSeeder.WebAndMailTemplateId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["@", "mail"],
            result.Value.SkippedTemplateRecords.Select(skipped =>
            {
                return skipped.Name;
            }));
        Assert.All(result.Value.SkippedTemplateRecords, skipped =>
        {
            Assert.Equal(RecordType.Aaaa, skipped.Type);
            Assert.Equal("{ipv6}", skipped.Token);
            Assert.Equal("DnsTemplateSkippedIpv6", skipped.Explanation);
            Assert.Equal("AAAA", skipped.TypeLabel);
        });
        Assert.DoesNotContain(context.Agent.Puts.Single().Records, record =>
        {
            return record.Kind == DnsRecordKind.Aaaa;
        });
        Assert.Equal(result.Value.RecordCount, context.Agent.Puts.Single().Records.Count);
    }

    /// <summary>Inverse control: with the address stated the AAAA records are created and nothing is skipped.</summary>
    [Fact]
    public async Task A_stated_setting_creates_its_records()
    {
        using var context = await WorldAsync();
        var settings = await context.Database.DnsSettings.SingleAsync();
        settings.Save("ns1.panel.test", "ns2.panel.test", "hostmaster.panel.test", "203.0.113.5", "2001:db8::5", 3600, context.Clock.UtcNow);
        await context.Database.SaveChangesAsync();

        var result = await context.CreateZone.HandleAsync(
            Create("shop.example", ZoneTemplateSeeder.WebTemplateId), CancellationToken.None);

        Assert.Empty(result.Value.SkippedTemplateRecords);
        Assert.Equal(3, result.Value.RecordCount);
    }

    /// <summary>A zone made without a template is empty and reports nothing skipped, as it always was.</summary>
    [Fact]
    public async Task A_zone_without_a_template_is_empty()
    {
        using var context = await WorldAsync();

        var result = await context.CreateZone.HandleAsync(Create("shop.example", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.RecordCount);
        Assert.Empty(result.Value.SkippedTemplateRecords);
        Assert.Empty(Assert.Single(context.Agent.Puts).Records);
    }

    /// <summary>A template that does not exist is not found, journalled, and nothing reaches the host.</summary>
    [Fact]
    public async Task An_unknown_template_is_not_found_and_nothing_reaches_the_host()
    {
        using var context = await WorldAsync();

        var result = await context.CreateZone.HandleAsync(Create("shop.example", Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("ZoneTemplateNotFound", result.Error!.Code);
        Assert.Empty(context.Agent.Puts);
        Assert.Empty(context.Database.Zones.AsNoTracking());
        var entry = context.Entries[^1];
        Assert.Equal(DnsAuditActions.DnsZoneCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The record cap still applies to a zone made from a template: over it, nothing is served.</summary>
    [Fact]
    public async Task The_record_cap_applies_to_a_template_zone()
    {
        using var context = await WorldAsync();
        context.Options.Value.MaxRecordsPerZone = 1;

        var over = await context.CreateZone.HandleAsync(
            Create("one.example", ZoneTemplateSeeder.WebTemplateId), CancellationToken.None);
        context.Options.Value.MaxRecordsPerZone = 2;
        var atCap = await context.CreateZone.HandleAsync(
            Create("two.example", ZoneTemplateSeeder.WebTemplateId), CancellationToken.None);

        Assert.Equal("ZoneTemplateTooManyRecords", over.Error!.Code);
        Assert.Equal("two.example", Assert.Single(context.Agent.Puts).Name);
        Assert.True(atCap.IsSuccess);
    }

    /// <summary>A stored template the real settings turn into an invalid record is refused before the host is touched.</summary>
    [Fact]
    public async Task A_template_that_expands_to_an_invalid_record_is_refused_before_the_host()
    {
        using var context = await WorldAsync();
        context.Options.Value.MaxRecordsPerZone = 50;
        var saved = await context.SaveTemplate.HandleAsync(
            new SaveZoneTemplateCommand(
                "Long",
                string.Empty,
                [new TemplateRecordDraft(new string('a', 60) + "." + new string('b', 60), RecordType.A, null, "{ipv4}")]),
            CancellationToken.None);
        Assert.True(saved.IsSuccess);
        var longApex = string.Join('.', Enumerable.Repeat(new string('c', 60), 3)) + ".example";

        var result = await context.CreateZone.HandleAsync(Create(longApex, saved.Value.Id), CancellationToken.None);

        Assert.Equal("ZoneTemplateInvalid", result.Error!.Code);
        Assert.Empty(context.Agent.Puts);
    }

    /// <summary>When the host refuses the first publication nothing is stored, records included.</summary>
    [Fact]
    public async Task A_host_refusal_stores_neither_the_zone_nor_its_template_records()
    {
        using var context = await WorldAsync();
        context.Agent.FailNext = Error.Of("AgentValidationFailed", ErrorType.Validation);

        var result = await context.CreateZone.HandleAsync(
            Create("shop.example", ZoneTemplateSeeder.WebTemplateId), CancellationToken.None);

        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Empty(context.Database.Zones.AsNoTracking());
        Assert.Empty(context.Database.ZoneRecords.AsNoTracking());
    }

    /// <summary>The records a template made are the zone's own: the owner can change one and it becomes the customer's.</summary>
    [Fact]
    public async Task A_template_record_can_be_changed_and_then_belongs_to_the_customer()
    {
        using var context = await WorldAsync();
        var created = await context.CreateZone.HandleAsync(
            Create("shop.example", ZoneTemplateSeeder.WebTemplateId), CancellationToken.None);
        var record = await context.Database.ZoneRecords.AsNoTracking().SingleAsync(row => row.Name == "@");

        var changed = await context.ChangeRecord.HandleAsync(
            new ChangeRecordCommand(
                new RecordDraft("@", RecordType.A, 300, "198.51.100.9"), created.Value.Id, record.Id, "203.0.113.1", "tests"),
            CancellationToken.None);

        Assert.True(changed.IsSuccess);
        Assert.Equal(RecordOrigin.Customer, changed.Value.Origin);
        Assert.True(changed.Value.Editable);
    }
}
