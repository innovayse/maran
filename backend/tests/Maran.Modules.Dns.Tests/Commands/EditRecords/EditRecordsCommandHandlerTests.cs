using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Commands.ChangeRecord;
using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Commands.DeleteRecord;
using Maran.Modules.Dns.Commands.ReplaceRecords;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.Commands.EditRecords;

/// <summary>
/// What an edit of a zone does: the whole zone goes to the host first, the row changes only when the host
/// accepts it, the serial moves, every outcome is journalled, and a zone the caller cannot see is not found.
/// </summary>
public sealed class EditRecordsCommandHandlerTests
{
    /// <summary>The account that owns the zone.</summary>
    private static readonly Guid AccountId = new("22222222-2222-4222-8222-222222222222");

    /// <summary>Builds a world with the server on and one empty zone, as an administrator.</summary>
    /// <returns>The world and the zone's id.</returns>
    private static async Task<(DnsTestContext Context, Guid ZoneId)> WorldWithZoneAsync()
    {
        var context = new DnsTestContext();
        context.Accounts.Add(AccountId, "bob", maxDnsZones: 5);
        await context.EnableDnsServerAsync();
        var created = await context.CreateZone.HandleAsync(
            new CreateZoneCommand(AccountId, "example.com", "203.0.113.1", "tests"), CancellationToken.None);

        return (context, created.Value.Id);
    }

    /// <summary>Builds an A record draft.</summary>
    /// <param name="name">The owner name.</param>
    /// <param name="address">The address.</param>
    /// <returns>The draft.</returns>
    private static RecordDraft A(string name, string address = "203.0.113.7")
    {
        return new RecordDraft(name, RecordType.A, 300, address);
    }

    /// <summary>Adding a record sends the WHOLE zone to the host with the new serial, then stores the row.</summary>
    [Fact]
    public async Task Adding_a_record_publishes_the_whole_zone_then_stores_it()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;

        var result = await context.AddRecord.HandleAsync(
            new AddRecordCommand(A("www"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = context.Agent.Puts[^1];
        Assert.Equal("www", Assert.Single(sent.Records).Name);
        Assert.Equal(2026110701, sent.Serial);
        var stored = Assert.Single(context.Database.ZoneRecords);
        Assert.Equal("203.0.113.7", stored.Value);
        Assert.Equal(DnsAuditActions.DnsRecordAdded, context.Entries[^1].Action);
        Assert.True(context.Entries[^1].Succeeded);
    }

    /// <summary>A host that refuses the edit leaves the stored zone exactly as it was.</summary>
    [Fact]
    public async Task A_host_refusal_leaves_the_stored_zone_as_it_was()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;
        context.Agent.FailNext = Error.Of("AgentValidationFailed", ErrorType.Validation);

        var result = await context.AddRecord.HandleAsync(
            new AddRecordCommand(A("www"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        // Read without tracking: the handler's context still holds the zone it changed in memory (a request's
        // context is disposed after a refusal; this one is shared), so only the store shows what was saved.
        Assert.Empty(context.Database.ZoneRecords.AsNoTracking());
        Assert.Equal(2026110700, context.Database.Zones.AsNoTracking().Single().Serial);
        Assert.False(context.Entries[^1].Succeeded);
    }

    /// <summary>Each zone rule answers with its own typed code and leaves the host untouched.</summary>
    [Fact]
    public async Task A_rule_refusal_never_reaches_the_host()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;
        await context.AddRecord.HandleAsync(new AddRecordCommand(A("www"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        var puts = context.Agent.Puts.Count;

        var duplicate = await context.AddRecord.HandleAsync(
            new AddRecordCommand(A("www"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        var cname = await context.AddRecord.HandleAsync(
            new AddRecordCommand(new RecordDraft("www", RecordType.Cname, 300, "x.example.net"), zoneId, "203.0.113.1", "tests"),
            CancellationToken.None);

        Assert.Equal("RecordDuplicate", duplicate.Error!.Code);
        Assert.Equal("RecordCnameBesideOtherData", cname.Error!.Code);
        Assert.Equal(puts, context.Agent.Puts.Count);
    }

    /// <summary>A name too long to fit under THIS zone is refused with the shape code even though the boundary could not see the zone.</summary>
    [Fact]
    public async Task A_name_that_does_not_fit_under_the_zone_is_refused()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;
        var name = string.Join('.', Enumerable.Repeat(new string('a', 60), 4));

        var result = await context.AddRecord.HandleAsync(
            new AddRecordCommand(A(name), zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("RecordNameInvalid", result.Error!.Code);
    }

    /// <summary>The record limit of the zone is enforced.</summary>
    [Fact]
    public async Task A_full_zone_refuses_another_record()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;
        for (var index = 0; index < 3; index++)
        {
            await context.AddRecord.HandleAsync(
                new AddRecordCommand(A($"h{index}"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        }

        var result = await context.AddRecord.HandleAsync(
            new AddRecordCommand(A("extra"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("ZoneRecordLimitReached", result.Error!.Code);
    }

    /// <summary>Changing and deleting a record publish again and update the row.</summary>
    [Fact]
    public async Task Changing_and_deleting_publish_again_and_update_the_row()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;
        var added = await context.AddRecord.HandleAsync(
            new AddRecordCommand(A("www"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        var recordId = added.Value.Id;

        var changed = await context.ChangeRecord.HandleAsync(
            new ChangeRecordCommand(A("www", "203.0.113.99"), zoneId, recordId, "203.0.113.1", "tests"),
            CancellationToken.None);
        var removed = await context.DeleteRecord.HandleAsync(
            new DeleteRecordCommand(zoneId, recordId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.True(changed.IsSuccess);
        Assert.Equal("203.0.113.99", changed.Value.Value);
        Assert.True(removed.IsSuccess);
        Assert.Empty(context.Database.ZoneRecords);
        Assert.Empty(context.Agent.Puts[^1].Records);
        Assert.Equal(
            [DnsAuditActions.DnsZoneCreated, DnsAuditActions.DnsRecordAdded, DnsAuditActions.DnsRecordChanged, DnsAuditActions.DnsRecordDeleted],
            context.Entries.Select(entry =>
            {
                return entry.Action;
            }).ToArray());
    }

    /// <summary>A record id the zone does not hold is "not found" and is journalled as a failure.</summary>
    [Fact]
    public async Task An_unknown_record_is_not_found()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;

        var result = await context.DeleteRecord.HandleAsync(
            new DeleteRecordCommand(zoneId, Guid.NewGuid(), "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("RecordNotFound", result.Error!.Code);
        Assert.False(context.Entries[^1].Succeeded);
    }

    /// <summary>An import replaces the customer's records in one publish.</summary>
    [Fact]
    public async Task An_import_replaces_the_records_in_one_publish()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;
        await context.AddRecord.HandleAsync(new AddRecordCommand(A("old"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        var puts = context.Agent.Puts.Count;

        var result = await context.ReplaceRecords.HandleAsync(
            new ReplaceRecordsCommand([A("a"), A("b")], zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(puts + 1, context.Agent.Puts.Count);
        Assert.Equal(["a", "b"], context.Database.ZoneRecords.Select(record => record.Name).Order().ToArray());
    }

    /// <summary>With the name server off, an edit is refused before any change.</summary>
    [Fact]
    public async Task An_edit_is_refused_while_the_name_server_is_off()
    {
        var (context, zoneId) = await WorldWithZoneAsync();
        using var _ = context;
        var settings = context.Database.DnsSettings.Single();
        settings.Disable(context.Clock.UtcNow);
        await context.Database.SaveChangesAsync();

        var result = await context.AddRecord.HandleAsync(
            new AddRecordCommand(A("www"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("DnsNotEnabled", result.Error!.Code);
    }

    /// <summary>Another account's zone is "not found" for a customer, on every record operation, and nothing is sent to the host.</summary>
    [Fact]
    public async Task Another_accounts_zone_is_not_found_on_every_record_operation()
    {
        var (admin, zoneId) = await WorldWithZoneAsync();
        using var _ = admin;
        var otherAccount = Guid.NewGuid();
        using var intruder = new DnsTestContext(new CustomerCurrentUser(otherAccount), admin.DatabaseName);
        var puts = intruder.Agent.Puts.Count;

        var added = await intruder.AddRecord.HandleAsync(
            new AddRecordCommand(A("evil"), zoneId, "203.0.113.1", "tests"), CancellationToken.None);
        var changed = await intruder.ChangeRecord.HandleAsync(
            new ChangeRecordCommand(A("evil"), zoneId, Guid.NewGuid(), "203.0.113.1", "tests"), CancellationToken.None);
        var removed = await intruder.DeleteRecord.HandleAsync(
            new DeleteRecordCommand(zoneId, Guid.NewGuid(), "203.0.113.1", "tests"), CancellationToken.None);
        var replaced = await intruder.ReplaceRecords.HandleAsync(
            new ReplaceRecordsCommand([A("evil")], zoneId, "203.0.113.1", "tests"), CancellationToken.None);

        Assert.All(
            new[] { added.Error!, changed.Error!, removed.Error!, replaced.Error! },
            error =>
            {
                Assert.Equal("ZoneNotFound", error.Code);
                Assert.Equal(ErrorType.NotFound, error.Type);
            });
        Assert.Equal(puts, intruder.Agent.Puts.Count);
        Assert.Empty(intruder.Database.ZoneRecords);
    }
}
