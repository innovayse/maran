using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Modules.Notifications.Jobs;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What one retention pass over <c>notifications.PanelNotifications</c> removes, and that the
/// per-administrator dismissals of a removed entry leave with it (issues #142 and #164).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an integration test.</b> The cascade is a foreign key with
/// <c>ON DELETE CASCADE</c> in PostgreSQL, which the in-memory provider the module's unit tests
/// use does not enforce, so only a real database can say whether an entry's dismissals outlive it.
/// </para>
/// <para>
/// <b>No HTTP, on purpose.</b> Retention is a scheduled message handler with no endpoint; the host
/// is booted only to resolve the handler and the context from the real container.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class NotificationRetentionTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Comfortably outside the retention window, whatever hour the suite runs at.</summary>
    private static readonly TimeSpan WellOutsideTheWindow = NotificationRetentionHandler.RetentionWindow
        + TimeSpan.FromDays(10);

    /// <summary>Comfortably inside the retention window.</summary>
    private static readonly TimeSpan WellInsideTheWindow = TimeSpan.FromDays(1);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public NotificationRetentionTests(PostgresFixture postgres)
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

    /// <summary>
    /// A pass past the window removes the old entry and every administrator's dismissal of it, and
    /// leaves a recent entry and its dismissals exactly where they were.
    /// </summary>
    [Fact]
    public async Task An_entry_past_the_window_leaves_with_its_dismissals_and_a_recent_one_keeps_them()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var now = Now(factory);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var old = New(now - WellOutsideTheWindow, "old");
        var recent = New(now - WellInsideTheWindow, "recent");
        await SeedAsync(factory, [old, recent], dismissals:
        [
            new NotificationDismissal(old.Id, first, now),
            new NotificationDismissal(old.Id, second, now),
            new NotificationDismissal(recent.Id, first, now),
            new NotificationDismissal(recent.Id, second, now),
        ]);

        Assert.Equal(1, await RunPassAsync(factory));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Equal([recent.Id], await dbContext.PanelNotifications.Select(row => row.Id).ToArrayAsync());

        // The assertion that matters: the dismissals of the removed entry are gone with it, and the
        // survivor's are untouched. A count alone would also pass a cascade that took everything.
        var remaining = await dbContext.NotificationDismissals.ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.All(remaining, dismissal =>
        {
            Assert.Equal(recent.Id, dismissal.NotificationId);
        });
    }

    /// <summary>
    /// A pass over a table longer than the kept count removes the oldest surplus and their
    /// dismissals, and keeps the newest entries with theirs.
    /// </summary>
    [Fact]
    public async Task Entries_beyond_the_kept_count_leave_with_their_dismissals()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var now = Now(factory);
        var administrator = Guid.NewGuid();
        const int surplus = 2;

        // Minutes apart, all inside the window, so only the kept-count rule can remove anything.
        var entries = Enumerable.Range(0, NotificationRetentionHandler.MaxKept + surplus)
            .Select(index =>
            {
                return New(now - WellInsideTheWindow - TimeSpan.FromMinutes(index), $"entry-{index}");
            })
            .ToList();
        var oldestIds = entries.TakeLast(surplus).Select(entry =>
        {
            return entry.Id;
        }).ToList();
        var newest = entries[0];
        await SeedAsync(factory, entries, dismissals:
        [
            new NotificationDismissal(oldestIds[0], administrator, now),
            new NotificationDismissal(oldestIds[1], administrator, now),
            new NotificationDismissal(newest.Id, administrator, now),
        ]);

        Assert.Equal(surplus, await RunPassAsync(factory));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Equal(NotificationRetentionHandler.MaxKept, await dbContext.PanelNotifications.CountAsync());
        Assert.Empty(await dbContext.PanelNotifications.Where(row => oldestIds.Contains(row.Id)).ToListAsync());

        var remaining = Assert.Single(await dbContext.NotificationDismissals.ToListAsync());
        Assert.Equal(newest.Id, remaining.NotificationId);
    }

    /// <summary>A pass over entries that are all recent and few removes nothing at all.</summary>
    [Fact]
    public async Task A_pass_over_recent_entries_within_the_kept_count_removes_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var now = Now(factory);
        var entry = New(now - WellInsideTheWindow, "recent");
        await SeedAsync(
            factory,
            [entry],
            dismissals: [new NotificationDismissal(entry.Id, Guid.NewGuid(), now)]);

        Assert.Equal(0, await RunPassAsync(factory));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Equal(1, await dbContext.PanelNotifications.CountAsync());
        Assert.Equal(1, await dbContext.NotificationDismissals.CountAsync());
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    /// <returns>The factory.</returns>
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

    /// <summary>Applies the Notifications schema this test's fresh database does not have yet.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Resolves once the tables exist.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }

    /// <summary>The instant the panel's own clock reports, which the window is measured against.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The clock's current instant.</returns>
    private static DateTimeOffset Now(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
    }

    /// <summary>Builds one entry.</summary>
    /// <param name="occurredAt">When it happened.</param>
    /// <param name="subject">What it is about.</param>
    /// <returns>The entry.</returns>
    private static PanelNotification New(DateTimeOffset occurredAt, string subject)
    {
        return new PanelNotification(
            Guid.NewGuid(),
            NotificationSource.Alert,
            "Test",
            NotificationSeverity.Info,
            subject,
            PanelNotificationArguments.None,
            deepLink: null,
            occurredAt);
    }

    /// <summary>Writes the entries, and then the dismissals that refer to them.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="entries">The entries to store.</param>
    /// <param name="dismissals">The dismissals to store beside them.</param>
    /// <returns>Resolves once both are stored.</returns>
    private static async Task SeedAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<PanelNotification> entries,
        IReadOnlyList<NotificationDismissal> dismissals)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        dbContext.PanelNotifications.AddRange(entries);
        await dbContext.SaveChangesAsync();
        dbContext.NotificationDismissals.AddRange(dismissals);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Runs one retention pass the way the daily scheduler's message does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>How many entries the pass removed.</returns>
    private static async Task<int> RunPassAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<NotificationRetentionHandler>();
        return await handler.HandleAsync(new NotificationRetentionRequested(), CancellationToken.None);
    }
}
