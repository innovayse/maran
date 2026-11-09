using Maran.Modules.Notifications.Commands.SaveAlertRecipients;
using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Notifications.Tests.Commands.SaveAlertRecipients;

/// <summary>Saving the alert recipient list, and the copy of its first entry the previous release reads.</summary>
public sealed class SaveAlertRecipientsCommandHandlerTests
{
    /// <summary>The instant the fixtures' rows are stamped with.</summary>
    private static readonly DateTimeOffset Saved = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The stored list becomes the requested one, lower-cased and in the requested order.</summary>
    [Fact]
    public async Task The_stored_list_becomes_the_requested_one_in_order()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var handler = Handler(dbContext, new RecordingAuditWriter());

        await handler.HandleAsync(Command("Second@Example.com", "first@example.com"), CancellationToken.None);

        var stored = await dbContext.AlertRecipients.OrderBy(row => row.Position).Select(row => row.Address).ToListAsync();
        Assert.Equal(["second@example.com", "first@example.com"], stored);
    }

    /// <summary>A kept address keeps its row; a removed one is deleted; a new one is added.</summary>
    [Fact]
    public async Task A_replace_keeps_moves_removes_and_adds_rows()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var kept = new AlertRecipient(Guid.NewGuid(), "keep@example.com", 0, Saved);
        dbContext.AlertRecipients.Add(kept);
        dbContext.AlertRecipients.Add(new AlertRecipient(Guid.NewGuid(), "gone@example.com", 1, Saved));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Handler(dbContext, new RecordingAuditWriter())
            .HandleAsync(Command("new@example.com", "keep@example.com"), CancellationToken.None);

        var rows = await dbContext.AlertRecipients.OrderBy(row => row.Position).ToListAsync();
        Assert.Equal(["new@example.com", "keep@example.com"], rows.Select(row => { return row.Address; }));
        Assert.Equal(kept.Id, rows[1].Id);
    }

    /// <summary>The first recipient is mirrored into the mail settings, and an empty list clears the mirror.</summary>
    [Fact]
    public async Task The_first_recipient_is_mirrored_into_the_mail_settings()
    {
        await using var dbContext = NotificationsTestContext.Create();
        dbContext.SmtpSettings.Add(new SmtpSettings(
            "smtp.example.com", 587, SmtpSecurity.StartTls, string.Empty, string.Empty,
            "panel@example.com", "Panel", Saved));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = Handler(dbContext, new RecordingAuditWriter());

        await handler.HandleAsync(Command("first@example.com", "second@example.com"), CancellationToken.None);
        Assert.Equal("first@example.com", (await dbContext.SmtpSettings.SingleAsync()).AlertRecipientAddress);

        await handler.HandleAsync(Command(), CancellationToken.None);
        Assert.Null((await dbContext.SmtpSettings.SingleAsync()).AlertRecipientAddress);
    }

    /// <summary>Saving with no mail settings row stores the list and does not fail.</summary>
    [Fact]
    public async Task Saving_without_mail_settings_stores_the_list()
    {
        await using var dbContext = NotificationsTestContext.Create();

        var result = await Handler(dbContext, new RecordingAuditWriter())
            .HandleAsync(Command("first@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(await dbContext.AlertRecipients.ToListAsync());
    }

    /// <summary>The audit entry carries the count and never an address.</summary>
    [Fact]
    public async Task The_audit_entry_names_the_count_and_never_an_address()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var audit = new RecordingAuditWriter();

        await Handler(dbContext, audit).HandleAsync(Command("a@example.com", "b@example.com"), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(OutgoingMailAuditActions.AlertRecipientsSaved, entry.Action);
        Assert.Equal("2", entry.Subject);
        Assert.DoesNotContain("example.com", System.Text.Json.JsonSerializer.Serialize(audit.Entries), StringComparison.Ordinal);
    }

    /// <summary>Builds the handler over the doubles a test supplies.</summary>
    /// <param name="dbContext">The context.</param>
    /// <param name="audit">The journal double.</param>
    /// <returns>The handler.</returns>
    private static SaveAlertRecipientsCommandHandler Handler(
        Persistence.NotificationsDbContext dbContext,
        Sdk.Interfaces.IAuditWriter audit)
    {
        return new SaveAlertRecipientsCommandHandler(
            dbContext, new NotificationsAuditJournal(audit, new FakeCurrentUser()), new FakeClock());
    }

    /// <summary>Builds a command carrying the given addresses.</summary>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The command.</returns>
    private static SaveAlertRecipientsCommand Command(params string[] addresses)
    {
        return new SaveAlertRecipientsCommand(addresses, "203.0.113.7", "curl/8");
    }
}
