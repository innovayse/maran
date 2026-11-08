using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Services;

/// <summary>The addresses another module may read from this one, and nothing wider.</summary>
public sealed class AlertRecipientDirectoryTests
{
    /// <summary>The instant the fixtures' rows are stamped with.</summary>
    private static readonly DateTimeOffset Saved = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A panel with no recipients answers an empty list, not a failure.</summary>
    [Fact]
    public async Task A_panel_with_no_recipients_answers_an_empty_list()
    {
        await using var dbContext = NotificationsTestContext.Create();

        var directory = new AlertRecipientDirectory(dbContext);

        Assert.Empty(await directory.GetAlertRecipientsAsync(CancellationToken.None));
    }

    /// <summary>The recipients are answered in list order, whatever order they were written in.</summary>
    [Fact]
    public async Task Recipients_are_answered_in_list_order()
    {
        await using var dbContext = NotificationsTestContext.Create();
        dbContext.AlertRecipients.Add(new AlertRecipient(Guid.NewGuid(), "second@example.com", 1, Saved));
        dbContext.AlertRecipients.Add(new AlertRecipient(Guid.NewGuid(), "first@example.com", 0, Saved));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var directory = new AlertRecipientDirectory(dbContext);

        Assert.Equal(
            ["first@example.com", "second@example.com"],
            await directory.GetAlertRecipientsAsync(CancellationToken.None));
    }

    /// <summary>The answer carries addresses only: a mail settings row with a password adds nothing to it.</summary>
    [Fact]
    public async Task The_mail_settings_are_not_part_of_the_answer()
    {
        await using var dbContext = NotificationsTestContext.Create();
        dbContext.SmtpSettings.Add(new SmtpSettings(
            "smtp.example.com", 587, SmtpSecurity.StartTls, "panel", "hunter2",
            "panel@example.com", "Panel", Saved));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var directory = new AlertRecipientDirectory(dbContext);

        Assert.Empty(await directory.GetAlertRecipientsAsync(CancellationToken.None));
    }
}
