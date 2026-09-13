using Maran.Modules.Notifications.Commands.SaveSmtpSettings;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Tests.Commands.SaveSmtpSettings;

/// <summary>Saving the panel's one row of mail settings, and what has to happen around the write.</summary>
public sealed class SaveSmtpSettingsCommandHandlerTests
{
    /// <summary>The first save creates the singleton row, under the fixed key.</summary>
    [Fact]
    public async Task The_first_save_creates_the_singleton_row()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext);
        var clock = new FakeClock();

        var handler = new SaveSmtpSettingsCommandHandler(
            dbContext, scopes.Settings, new NotificationsAuditJournal(audit, new FakeCurrentUser()), clock);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(dbContext.SmtpSettings);
        Assert.Equal(Notifications.Domain.Entities.SmtpSettings.SingletonId, row.Id);
        Assert.Equal("smtp.example.com", row.Host);
        Assert.Equal(clock.UtcNow, row.UpdatedAt);
    }

    /// <summary>Saving twice replaces the one row rather than adding a second.</summary>
    /// <remarks>
    /// Two rows would be two answers to "where does the panel's mail go", and whichever the reader
    /// loaded first would be the one that took effect.
    /// </remarks>
    [Fact]
    public async Task Saving_twice_replaces_the_one_row_rather_than_adding_a_second()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext);

        var handler = new SaveSmtpSettingsCommandHandler(
            dbContext, scopes.Settings, new NotificationsAuditJournal(audit, new FakeCurrentUser()), new FakeClock());

        await handler.HandleAsync(Command(), CancellationToken.None);
        await handler.HandleAsync(Command() with { Host = "smtp.example.net", Port = 465 }, CancellationToken.None);

        var row = Assert.Single(dbContext.SmtpSettings);
        Assert.Equal("smtp.example.net", row.Host);
        Assert.Equal(465, row.Port);
    }

    /// <summary>A save with no password keeps the one already stored.</summary>
    /// <remarks>
    /// The settings form cannot show the stored password, so it submits none when the administrator
    /// did not retype one. Reading that as "clear it" would unauthenticate the panel's mail the first
    /// time anybody changed the port.
    /// </remarks>
    [Fact]
    public async Task A_save_with_no_password_keeps_the_one_already_stored()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext);

        var handler = new SaveSmtpSettingsCommandHandler(
            dbContext, scopes.Settings, new NotificationsAuditJournal(audit, new FakeCurrentUser()), new FakeClock());

        await handler.HandleAsync(Command(), CancellationToken.None);
        await handler.HandleAsync(Command() with { Password = null, Port = 2525 }, CancellationToken.None);

        var row = Assert.Single(dbContext.SmtpSettings);
        Assert.Equal("hunter2", row.Password);
        Assert.Equal(2525, row.Port);
    }

    /// <summary>The cache is dropped, so the very next send goes through the new server.</summary>
    /// <remarks>
    /// The sender reads its settings from a process-lifetime cache (R12). Without the invalidation the
    /// panel would keep sending through the old server until it was restarted, which is the shape of
    /// bug an administrator debugging their mail cannot see at all.
    /// </remarks>
    [Fact]
    public async Task Saving_drops_the_senders_cached_settings()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext);

        // Warm the cache while the panel has no settings at all, which is the state a fresh
        // installation's first alert evaluation leaves it in.
        Assert.Null(await scopes.Settings.GetAsync(CancellationToken.None));

        var handler = new SaveSmtpSettingsCommandHandler(
            dbContext, scopes.Settings, new NotificationsAuditJournal(audit, new FakeCurrentUser()), new FakeClock());
        await handler.HandleAsync(Command(), CancellationToken.None);

        var profile = await scopes.Settings.GetAsync(CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("smtp.example.com", profile!.Host);
        Assert.Equal("ops@example.com", profile.AlertRecipient);
    }

    /// <summary>The audit entry names the mail server and never the credential.</summary>
    /// <remarks>
    /// The journal is append-only and never deleted, so a password in it is a password kept for ever,
    /// in a place an operator reads (rules/security.md item 8).
    /// </remarks>
    [Fact]
    public async Task The_audit_entry_names_the_server_and_never_the_credential()
    {
        await using var dbContext = NotificationsTestContext.Create();
        var audit = new RecordingAuditWriter();
        using var scopes = new TestScopeFactory(dbContext);

        var handler = new SaveSmtpSettingsCommandHandler(
            dbContext, scopes.Settings, new NotificationsAuditJournal(audit, new FakeCurrentUser()), new FakeClock());
        await handler.HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.SmtpSettingsSaved, entry.Action);
        Assert.Equal("smtp.example.com", entry.Subject);

        var serialized = System.Text.Json.JsonSerializer.Serialize(audit.Entries);
        Assert.DoesNotContain("hunter2", serialized, StringComparison.Ordinal);
    }

    /// <summary>A configuration a validator would accept.</summary>
    /// <returns>The command.</returns>
    private static SaveSmtpSettingsCommand Command()
    {
        return new SaveSmtpSettingsCommand(
            "smtp.example.com",
            587,
            SmtpSecurity.StartTls,
            "panel",
            "hunter2",
            "panel@example.com",
            "Maran Panel",
            "ops@example.com",
            "203.0.113.7",
            "curl/8");
    }
}
