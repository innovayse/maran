using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Queries.GetSmtpSettings;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Queries.GetSmtpSettings;

/// <summary>What the settings screen reads back, and above all what it does not.</summary>
public sealed class GetSmtpSettingsQueryHandlerTests
{
    /// <summary>The instant the fixture's settings were saved.</summary>
    private static readonly DateTimeOffset Saved = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The settings read back carry a flag saying a password exists, and nowhere for its value.</summary>
    /// <remarks>
    /// Asserted over the whole serialized answer rather than field by field, because the guarantee is
    /// that the value is absent from ALL of it: a future field carrying the password would pass a
    /// per-field assertion and fail this one.
    /// </remarks>
    [Fact]
    public async Task The_settings_read_back_never_carry_the_password()
    {
        await using var dbContext = NotificationsTestContext.Create();
        dbContext.SmtpSettings.Add(Configured());
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSmtpSettingsQueryHandler(dbContext);

        var result = await handler.HandleAsync(new GetSmtpSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasPassword);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
    }

    /// <summary>The rest of the settings do come back, so the form can be filled in.</summary>
    [Fact]
    public async Task The_settings_read_back_carry_everything_except_the_password()
    {
        await using var dbContext = NotificationsTestContext.Create();
        dbContext.SmtpSettings.Add(Configured());
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSmtpSettingsQueryHandler(dbContext);

        var result = await handler.HandleAsync(new GetSmtpSettingsQuery(), CancellationToken.None);

        Assert.Equal("smtp.example.com", result.Value.Host);
        Assert.Equal(587, result.Value.Port);
        Assert.Equal(SmtpSecurity.StartTls, result.Value.Security);
        Assert.Equal("panel", result.Value.Username);
        Assert.Equal("panel@example.com", result.Value.FromAddress);
        Assert.Equal("ops@example.com", result.Value.AlertRecipient);
        Assert.Equal(Saved, result.Value.UpdatedAt);
    }

    /// <summary>A panel that has never configured mail reads back blank settings rather than a failure.</summary>
    /// <remarks>
    /// A fresh installation is the ordinary state, and a 4xx here would make the settings screen show
    /// an error where it should show an empty form. The null <c>UpdatedAt</c> is what says "never
    /// saved" without the panel inventing a plausible-looking mail server.
    /// </remarks>
    [Fact]
    public async Task A_panel_with_no_settings_reads_back_blank_ones_rather_than_a_failure()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var handler = new GetSmtpSettingsQueryHandler(dbContext);

        var result = await handler.HandleAsync(new GetSmtpSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value.Host);
        Assert.False(result.Value.HasPassword);
        Assert.Null(result.Value.UpdatedAt);
    }

    /// <summary>A panel whose mail server takes no credentials reports that no password is stored.</summary>
    [Fact]
    public async Task A_server_that_takes_no_credentials_reports_no_stored_password()
    {
        await using var dbContext = NotificationsTestContext.Create();
        dbContext.SmtpSettings.Add(new SmtpSettings(
            "127.0.0.1", 25, SmtpSecurity.None, string.Empty, string.Empty,
            "panel@example.com", "Panel", "ops@example.com", Saved));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSmtpSettingsQueryHandler(dbContext);

        var result = await handler.HandleAsync(new GetSmtpSettingsQuery(), CancellationToken.None);

        Assert.False(result.Value.HasPassword);
    }

    /// <summary>Settings as a panel that has already configured mail holds them.</summary>
    /// <returns>The settings.</returns>
    private static SmtpSettings Configured()
    {
        return new SmtpSettings(
            "smtp.example.com",
            587,
            SmtpSecurity.StartTls,
            "panel",
            "hunter2",
            "panel@example.com",
            "Panel",
            "ops@example.com",
            Saved);
    }
}
