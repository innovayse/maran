using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Services;

/// <summary>
/// The cache's one dangerous moment: a save that commits while a load is already in flight.
/// </summary>
public sealed class SmtpSettingsCacheTests
{
    /// <summary>The instant the fixtures' settings rows are stamped with.</summary>
    private static readonly DateTimeOffset Saved = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A save committed while a load is in flight is not overwritten by that load.</summary>
    /// <remarks>
    /// <para>
    /// The load is suspended inside the cache after it has READ the old row and before it can
    /// publish it, by a scope whose disposal blocks. The row is then changed and
    /// <see cref="SmtpSettingsCache.Invalidate"/> is called from another thread, which is the exact
    /// interleaving the ordering rule ("invalidate after the commit") does not by itself cover.
    /// </para>
    /// <para>
    /// Why it matters more here than in most caches: this one has no expiry by design, so a lost
    /// invalidation is permanent. The operator moves the panel to a new mail server, the settings
    /// screen shows the new values because it reads the row rather than the cache, and every alert
    /// and password-reset mail keeps leaving through the old server until the process restarts.
    /// </para>
    /// <para>
    /// <b>The mutation that reddens this:</b> take the <c>_gate.Wait()</c>/<c>Release()</c> pair out
    /// of <c>Invalidate</c> and let it clear the two fields directly — which is what the code did
    /// before this change. The suspended load then publishes the pre-save profile after the
    /// invalidation has already run, <c>_loaded</c> goes back to true, and the final read below
    /// answers <c>old.example.com</c> for ever. Verified by making exactly that edit and watching
    /// this test fail on <c>Assert.Equal("new.example.com", current.Host)</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_save_committed_while_a_load_is_in_flight_is_not_overwritten_by_that_load()
    {
        await using var dbContext = NotificationsTestContext.Create();
        await SeedAsync(dbContext, "old.example.com");

        using var scopes = new BlockingScopeFactory(dbContext);
        using var cache = new SmtpSettingsCache(scopes);

        // The load takes the cache's gate, queries, and then blocks in its scope's disposal — after
        // it has read the old row and before it can publish it.
        var load = Task.Run(async () =>
        {
            return await cache.GetAsync(CancellationToken.None);
        });
        scopes.WaitUntilRowRead();

        // The administrator's save: commit first, then invalidate — the documented order, which is
        // exactly what is not sufficient on its own.
        await SeedAsync(dbContext, "new.example.com");

        var invalidationEntered = new ManualResetEventSlim(false);
        var invalidation = Task.Run(() =>
        {
            invalidationEntered.Set();
            cache.Invalidate();
        });

        // Two waits, and the second is what makes the fixture decide rather than race. The first
        // says the invalidating thread is running. The second gives it time to finish: with the gate
        // in place it CANNOT finish — it is blocked behind the suspended load — so this times out and
        // the test carries on, while a gateless Invalidate completes here and its clearing is then
        // guaranteed to precede the load's publish. Without it the two could go in either order and
        // the broken cache would pass about as often as not.
        invalidationEntered.Wait(TimeSpan.FromSeconds(10));
        await Task.WhenAny(invalidation, Task.Delay(TimeSpan.FromMilliseconds(250)));

        scopes.ReleaseLoad();
        await load;
        await invalidation;

        var current = await cache.GetAsync(CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal("new.example.com", current.Host);
    }

    /// <summary>A load with no in flight save is cached and served without a second query.</summary>
    /// <remarks>
    /// Guards the fix from being "correct" by never caching at all: the scope factory here is
    /// released once, so a second read that went to the database would block for ten seconds and
    /// fail rather than answer.
    /// </remarks>
    [Fact]
    public async Task A_loaded_profile_is_served_from_memory_on_the_next_read()
    {
        await using var dbContext = NotificationsTestContext.Create();
        await SeedAsync(dbContext, "smtp.example.com");

        using var scopes = new BlockingScopeFactory(dbContext);
        using var cache = new SmtpSettingsCache(scopes);

        scopes.ReleaseLoad();
        var first = await cache.GetAsync(CancellationToken.None);
        var second = await cache.GetAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    /// <summary>Writes or replaces the settings row, pointing it at the given host.</summary>
    /// <param name="dbContext">The context to seed.</param>
    /// <param name="host">The mail server to store.</param>
    /// <returns>Resolves once the row is saved.</returns>
    private static async Task SeedAsync(NotificationsDbContext dbContext, string host)
    {
        var existing = await dbContext.SmtpSettings.FindAsync(SmtpSettings.SingletonId);

        if (existing is null)
        {
            dbContext.SmtpSettings.Add(new SmtpSettings(
                host, 587, SmtpSecurity.StartTls, "panel", "hunter2",
                "panel@example.com", "Panel", "ops@example.com", Saved));
        }
        else
        {
            existing.Replace(
                host, 587, SmtpSecurity.StartTls, "panel", "hunter2",
                "panel@example.com", "Panel", "ops@example.com", Saved);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
