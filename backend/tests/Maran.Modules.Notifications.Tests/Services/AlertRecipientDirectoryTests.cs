using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Services;

/// <summary>
/// The one field of the mail settings another module may read, and the three answers it can give.
/// </summary>
public sealed class AlertRecipientDirectoryTests
{
    /// <summary>The instant the fixtures' settings row is stamped with.</summary>
    private static readonly DateTimeOffset Saved = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A panel with no mail settings at all answers null rather than an empty address.</summary>
    [Fact]
    public async Task A_panel_with_no_settings_has_no_alert_recipient()
    {
        await using var dbContext = NotificationsTestContext.Create();
        using var scopes = new TestScopeFactory(dbContext);

        var directory = new AlertRecipientDirectory(scopes.Settings);

        Assert.Null(await directory.GetAlertRecipientAsync(CancellationToken.None));
    }

    /// <summary>A configured alert address is answered as it is stored.</summary>
    [Fact]
    public async Task A_configured_alert_address_is_answered()
    {
        await using var dbContext = NotificationsTestContext.Create();
        await SeedAsync(dbContext, "ops@example.com");
        using var scopes = new TestScopeFactory(dbContext);

        var directory = new AlertRecipientDirectory(scopes.Settings);

        Assert.Equal("ops@example.com", await directory.GetAlertRecipientAsync(CancellationToken.None));
    }

    /// <summary>Settings saved with a blank alert address answer null, not whitespace.</summary>
    /// <remarks>
    /// An ordinary state: the settings screen saves a host and a sender before an operator has
    /// decided where alerts should go. Answering the whitespace would have the caller ask for a mail
    /// to an empty address, which fails inside the mail server instead of being recorded as the
    /// configuration gap it is. The mutation is dropping the <c>IsNullOrWhiteSpace</c> arm.
    /// </remarks>
    [Fact]
    public async Task A_blank_alert_address_is_answered_as_none()
    {
        await using var dbContext = NotificationsTestContext.Create();
        await SeedAsync(dbContext, "   ");
        using var scopes = new TestScopeFactory(dbContext);

        var directory = new AlertRecipientDirectory(scopes.Settings);

        Assert.Null(await directory.GetAlertRecipientAsync(CancellationToken.None));
    }

    /// <summary>Writes one settings row carrying the given alert address.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="alertRecipient">Where alert mail is to go.</param>
    /// <returns>Resolves once the row is saved.</returns>
    private static async Task SeedAsync(NotificationsDbContext dbContext, string alertRecipient)
    {
        dbContext.SmtpSettings.Add(new SmtpSettings(
            "smtp.example.com",
            587,
            SmtpSecurity.StartTls,
            "panel",
            "hunter2",
            "panel@example.com",
            "Panel",
            alertRecipient,
            Saved));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
