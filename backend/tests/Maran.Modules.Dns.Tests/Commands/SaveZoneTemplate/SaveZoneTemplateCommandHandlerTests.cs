using Maran.Modules.Dns.Commands.SaveZoneTemplate;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.SaveZoneTemplate;

/// <summary>
/// Saving a template: it is weighed as a whole by the law of a zone, the name is unique without regard to case,
/// a replacement replaces every record, and every outcome is journalled. Each refusal has an accepting control.
/// </summary>
public sealed class SaveZoneTemplateCommandHandlerTests
{
    /// <summary>A record set every test can save: an A at the apex and a CNAME for www.</summary>
    private static readonly IReadOnlyList<TemplateRecordDraft> WebRecords =
    [
        new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}"),
        new TemplateRecordDraft("www", RecordType.Cname, null, "{apex}."),
    ];

    /// <summary>The owner names of a template one record over the test world's cap of three.</summary>
    private static readonly string[] Names = ["a", "b", "c", "d"];

    /// <summary>Builds a save command for a new template.</summary>
    /// <param name="name">The template's name.</param>
    /// <param name="records">The records.</param>
    /// <returns>The command.</returns>
    private static SaveZoneTemplateCommand Create(string name, IReadOnlyList<TemplateRecordDraft> records)
    {
        return new SaveZoneTemplateCommand(name, "about", records, Guid.Empty, "203.0.113.1", "tests");
    }

    /// <summary>Saving a new template stores it with its records in order and journals the save.</summary>
    [Fact]
    public async Task Saving_a_template_stores_it_and_journals_it()
    {
        using var context = new DnsTestContext();

        var result = await context.SaveTemplate.HandleAsync(Create("Starter", WebRecords), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Starter", result.Value.Name);
        Assert.False(result.Value.IsBuiltIn);
        Assert.Equal(["@", "www"], result.Value.Records.Select(record =>
        {
            return record.Name;
        }));
        var stored = await context.Database.ZoneTemplates.Include(row => row.Records).SingleAsync();
        Assert.Equal(2, stored.Records.Count);
        var entry = Assert.Single(context.Entries);
        Assert.Equal(DnsAuditActions.ZoneTemplateSaved, entry.Action);
        Assert.Equal("Starter", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A record that fails the text law when expanded is refused, and the same template with a valid one is stored.</summary>
    [Fact]
    public async Task A_record_that_expands_to_invalid_text_is_refused_and_a_valid_one_is_stored()
    {
        using var context = new DnsTestContext();
        var bad = new TemplateRecordDraft("@", RecordType.Txt, null, "line one\nline two {apex}");
        var good = new TemplateRecordDraft("@", RecordType.Txt, null, "v=spf1 mx ~all {apex}");

        var refused = await context.SaveTemplate.HandleAsync(Create("Bad", [bad]), CancellationToken.None);
        var accepted = await context.SaveTemplate.HandleAsync(Create("Good", [good]), CancellationToken.None);

        Assert.Equal("RecordValueInvalid", refused.Error!.Code);
        Assert.True(accepted.IsSuccess);
        Assert.Equal("Good", Assert.Single(context.Database.ZoneTemplates).Name);
        Assert.False(context.Entries[0].Succeeded);
    }

    /// <summary>An unknown variable is refused with its own code; a known one is accepted.</summary>
    [Fact]
    public async Task An_unknown_variable_is_refused()
    {
        using var context = new DnsTestContext();

        var refused = await context.SaveTemplate.HandleAsync(
            Create("Unknown", [new TemplateRecordDraft("@", RecordType.Txt, null, "{nope}")]), CancellationToken.None);
        var unbalanced = await context.SaveTemplate.HandleAsync(
            Create("Unbalanced", [new TemplateRecordDraft("@", RecordType.Txt, null, "{apex")]), CancellationToken.None);
        var placed = await context.SaveTemplate.HandleAsync(
            Create("Placed", [new TemplateRecordDraft("@", RecordType.A, null, "{apex}")]), CancellationToken.None);
        var known = await context.SaveTemplate.HandleAsync(
            Create("Known", [new TemplateRecordDraft("@", RecordType.Txt, null, "{apex}")]), CancellationToken.None);

        Assert.Equal("ZoneTemplateTokenUnknown", refused.Error!.Code);
        Assert.Equal("ZoneTemplateTokenUnbalanced", unbalanced.Error!.Code);
        Assert.Equal("ZoneTemplateTokenNotAllowed", placed.Error!.Code);
        Assert.True(known.IsSuccess);
    }

    /// <summary>The name is unique without regard to case or surrounding space, and the refusal stores nothing.</summary>
    [Fact]
    public async Task The_name_is_unique_without_regard_to_case()
    {
        using var context = new DnsTestContext();
        await context.SaveTemplate.HandleAsync(Create("Starter", WebRecords), CancellationToken.None);

        var taken = await context.SaveTemplate.HandleAsync(Create("  STARTER ", WebRecords), CancellationToken.None);
        var other = await context.SaveTemplate.HandleAsync(Create("Starter two", WebRecords), CancellationToken.None);

        Assert.Equal("ZoneTemplateNameTaken", taken.Error!.Code);
        Assert.True(other.IsSuccess);
        Assert.Equal(2, await context.Database.ZoneTemplates.CountAsync());
    }

    /// <summary>A template may be saved again under its own name: only another template's name is taken.</summary>
    [Fact]
    public async Task A_template_may_be_replaced_under_its_own_name()
    {
        using var context = new DnsTestContext();
        var created = await context.SaveTemplate.HandleAsync(Create("Starter", WebRecords), CancellationToken.None);

        var replaced = await context.SaveTemplate.HandleAsync(
            new SaveZoneTemplateCommand(
                "STARTER", "new about", [new TemplateRecordDraft("@", RecordType.A, 600, "{ipv4}")], created.Value.Id),
            CancellationToken.None);

        Assert.True(replaced.IsSuccess);
        Assert.Equal("STARTER", replaced.Value.Name);
        var record = Assert.Single(replaced.Value.Records);
        Assert.Equal(600, record.Ttl);
        Assert.Single(context.Database.ZoneTemplateRecords.AsNoTracking());
    }

    /// <summary>Replacing a template that does not exist is not found and creates nothing.</summary>
    [Fact]
    public async Task Replacing_a_missing_template_is_not_found()
    {
        using var context = new DnsTestContext();

        var result = await context.SaveTemplate.HandleAsync(
            new SaveZoneTemplateCommand("x", string.Empty, WebRecords, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("ZoneTemplateNotFound", result.Error!.Code);
        Assert.Empty(context.Database.ZoneTemplates);
    }

    /// <summary>A template with more records than a zone may hold is refused; one at the cap is stored.</summary>
    [Fact]
    public async Task A_template_over_the_record_cap_is_refused()
    {
        using var context = new DnsTestContext();
        var four = Names.Select(name =>
        {
            return new TemplateRecordDraft(name, RecordType.A, null, "{ipv4}");
        }).ToList();

        var over = await context.SaveTemplate.HandleAsync(Create("Over", four), CancellationToken.None);
        var atCap = await context.SaveTemplate.HandleAsync(Create("AtCap", four.Take(3).ToList()), CancellationToken.None);

        Assert.Equal("ZoneTemplateTooManyRecords", over.Error!.Code);
        Assert.True(atCap.IsSuccess);
    }

    /// <summary>Records that break the zone's rules between themselves are refused when the template is saved.</summary>
    [Fact]
    public async Task Records_that_clash_inside_the_template_are_refused()
    {
        using var context = new DnsTestContext();
        var clash = new[]
        {
            new TemplateRecordDraft("www", RecordType.A, null, "{ipv4}"),
            new TemplateRecordDraft("www", RecordType.Cname, null, "{apex}"),
        };
        var duplicate = new[]
        {
            new TemplateRecordDraft("www", RecordType.A, null, "{ipv4}"),
            new TemplateRecordDraft("WWW", RecordType.A, 600, "{ipv4}"),
        };

        var cname = await context.SaveTemplate.HandleAsync(Create("Clash", clash), CancellationToken.None);
        var twice = await context.SaveTemplate.HandleAsync(Create("Twice", duplicate), CancellationToken.None);

        Assert.Equal("RecordCnameBesideOtherData", cname.Error!.Code);
        Assert.Equal("RecordDuplicate", twice.Error!.Code);
        Assert.Empty(context.Database.ZoneTemplates);
    }

    /// <summary>A name the policy refuses is a validation failure, journalled as a refused save.</summary>
    [Fact]
    public async Task A_refused_name_is_a_validation_failure()
    {
        using var context = new DnsTestContext();

        var result = await context.SaveTemplate.HandleAsync(Create("tab\there", WebRecords), CancellationToken.None);

        Assert.Equal("ZoneTemplateNameInvalid", result.Error!.Code);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }
}
