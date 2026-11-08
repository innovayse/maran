using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The Notifications migration that introduces the alert recipient list carries the one alert
/// address the previous release kept in the mail settings into the list as its first entry
/// (issues #142 and #164).
/// </summary>
/// <remarks>
/// <para>
/// The database is first brought to the schema the PREVIOUS release shipped, a mail settings row
/// is written the way that release wrote it, and only then is the migration applied. Migrating a
/// fresh database all the way up would show an empty table and prove nothing about the data
/// statement, which only has an effect on an upgrade.
/// </para>
/// <para>
/// A blank address and a panel with no mail settings at all are the controls: they must yield no
/// row, and the padded, mixed-case address beside them must yield exactly one, so a statement
/// mutated to copy everything, or nothing, fails one of the three.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AlertRecipientsMigrationTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The last Notifications migration the previous release shipped.</summary>
    private const string PreviousRelease = "20261102124155_AddSmtpAuthenticationMode";

    /// <summary>The instant the settings row of the previous release was last saved.</summary>
    private static readonly DateTimeOffset SettingsSavedAt = new(2026, 11, 2, 13, 0, 0, TimeSpan.Zero);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AlertRecipientsMigrationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The stored alert address becomes the first recipient: trimmed, lower-cased, at position zero, with the settings' save time.</summary>
    [Fact]
    public async Task The_stored_alert_address_becomes_the_first_recipient()
    {
        await using var factory = CreateFactory();
        await MigrateToPreviousReleaseAsync(factory);
        await StoreSettingsAsync(factory, "  Ops@Example.COM  ");

        await MigrateToCurrentAsync(factory);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var row = Assert.Single(await dbContext.AlertRecipients.AsNoTracking().ToListAsync());
        Assert.Equal("ops@example.com", row.Address);
        Assert.Equal(0, row.Position);
        Assert.Equal(SettingsSavedAt, row.AddedAt);

        // And the window the alert senders use reads it, not only the table.
        var recipients = await scope.ServiceProvider.GetRequiredService<IAlertRecipientDirectory>()
            .GetAlertRecipientsAsync(CancellationToken.None);
        Assert.Equal(["ops@example.com"], recipients.ToArray());
    }

    /// <summary>A settings row with a blank alert address, or none, yields no recipient.</summary>
    /// <param name="stored">What the previous release had stored in the alert address column.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_stored_alert_address_yields_no_recipient(string? stored)
    {
        await using var factory = CreateFactory();
        await MigrateToPreviousReleaseAsync(factory);
        await StoreSettingsAsync(factory, stored);

        await MigrateToCurrentAsync(factory);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Empty(await dbContext.AlertRecipients.ToListAsync());
    }

    /// <summary>A panel with no mail settings at all yields no recipient and the migration still applies.</summary>
    [Fact]
    public async Task A_panel_with_no_mail_settings_yields_no_recipient()
    {
        await using var factory = CreateFactory();
        await MigrateToPreviousReleaseAsync(factory);

        await MigrateToCurrentAsync(factory);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Empty(await dbContext.AlertRecipients.ToListAsync());
        Assert.Empty(await dbContext.SmtpSettings.ToListAsync());
    }

    /// <summary>Brings the Notifications schema to what the previous release shipped.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Resolves once the older schema is in place.</returns>
    private static async Task MigrateToPreviousReleaseAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await dbContext.GetService<IMigrator>().MigrateAsync(PreviousRelease);
    }

    /// <summary>Applies every Notifications migration still pending, the way an upgrade does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Resolves once the schema is current.</returns>
    private static async Task MigrateToCurrentAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Writes the one mail settings row the way the previous release did, with the given alert address.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="alertAddress">The value of the previous release's alert address column.</param>
    /// <returns>Resolves once the row is stored.</returns>
    private static async Task StoreSettingsAsync(WebApplicationFactory<Program> factory, string? alertAddress)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        // raw-sql: writes a row in the previous release's schema, which no entity of this release
        // can describe; every value is a parameter and the statement runs only against a test database.
        await dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO notifications."SmtpSettings"
                ("Id", "Host", "Port", "Security", "Username", "Password", "FromAddress", "FromName",
                 "AlertRecipient", "UpdatedAt", "AlertRecipientAddress")
            VALUES
                ({Guid.NewGuid()}, 'smtp.example.com', 587, 1, '', '', 'panel@example.com', 'Panel',
                 '', {SettingsSavedAt}, CAST({alertAddress} AS character varying))
            """);
    }

    /// <summary>Boots the host against this test's database.</summary>
    /// <returns>The host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }
}
