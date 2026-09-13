using Maran.Modules.Notifications.Models;
using Maran.Modules.Notifications.Persistence;

namespace Maran.Modules.Notifications.Services;

/// <summary>
/// Holds the panel's one row of mail settings in memory, and forgets it the moment somebody saves
/// new ones (R12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a cache at all.</b> The settings are read on every send — every alert, every password
/// reset, every test — and they change perhaps twice in a server's life. Reading a single row per
/// send is not expensive on its own; what makes the cache worth its existence is that the sender
/// runs in the background, off the request path, where opening a scope and a database connection to
/// re-read an unchanged row is pure ceremony.
/// </para>
/// <para>
/// <b>Invalidation is a write-side responsibility, and it is a single call.</b> The save handler
/// calls <see cref="Invalidate"/> after it commits. There is deliberately no expiry: a time-based
/// cache would mean a saved setting takes effect at some unpredictable moment, which is exactly the
/// behaviour an administrator debugging their mail server cannot work with. The panel is one process
/// per server, so an invalidation reaches every sender by reaching this object.
/// </para>
/// <para>
/// <b>Because there is no expiry, a lost invalidation would be permanent</b>, so losing one must be
/// impossible rather than unlikely. Committing before invalidating is necessary but NOT sufficient:
/// a load that started before the save returns after it, and if it could publish what it read, the
/// pre-save profile would be re-cached for the life of the process. What that costs an operator is
/// specific and silent — they move the panel to a new mail server, the settings screen shows the new
/// values because it reads the ROW rather than this cache, and every alert and every password-reset
/// mail keeps going out through the old server until the process restarts.
/// <see cref="_gate"/> is what prevents it: a load holds the gate from before it queries until after
/// it publishes, and <see cref="Invalidate"/> takes the same gate, so an invalidation is never
/// observed by a load that is between reading a row and publishing it.
/// </para>
/// <para>
/// <b>There is deliberately no lock-free fast path, and removing it is what made the above true.</b>
/// This cache's loaded state is TWO facts — the profile, and whether a load happened at all, because
/// "the panel has no mail settings" is an ordinary answer that must itself be cached. Two fields
/// cannot be published in one atomic write, so a reader outside the gate can see one without the
/// other however carefully each is volatile; keeping the fast path would have meant inventing a
/// wrapper object whose only purpose was to make two fields into one reference. It would have bought
/// nothing: unlike the sign-in path, this is read by a background sender a handful of times a
/// minute, so the cost being avoided is an uncontended semaphore, and the cost being kept is a
/// database round trip. The gate is taken on every read instead, and the whole class of
/// memory-visibility question goes with the fast path.
/// </para>
/// <para>
/// <b>It is a singleton and it resolves a scoped context through a scope factory.</b> A singleton
/// that captured <c>NotificationsDbContext</c> directly is refused by the container at build time,
/// which stops the whole API rather than degrading one feature.
/// </para>
/// </remarks>
public sealed class SmtpSettingsCache : IDisposable
{
    /// <summary>Opens one scope per load to resolve the module's database context from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Serialises loads against each other and against invalidations, so a burst of alerts on a cold
    /// cache issues one query rather than one each, and no load can publish a row that a save has
    /// already superseded.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The cached settings, or <c>null</c> when the panel has none.</summary>
    /// <remarks>
    /// Only ever read and written while <see cref="_gate"/> is held, which is what makes it safe to
    /// be a plain field: there is no publication of it that another thread can observe out of order,
    /// because there is no read of it outside the lock.
    /// </remarks>
    private SmtpProfile? _profile;

    /// <summary>Whether <see cref="_profile"/> reflects a read that actually happened.</summary>
    /// <remarks>
    /// A separate flag from <see cref="_profile"/> because the ABSENCE of settings is cached too: a
    /// panel with no mail configured is the ordinary state of a fresh installation, and treating a
    /// null profile as "not loaded yet" would re-query the empty table on every alert evaluation for
    /// the life of the process. Being a second field is also why this cache has no lock-free read —
    /// see the type's remarks.
    /// </remarks>
    private bool _loaded;

    /// <summary>Creates the cache.</summary>
    /// <param name="scopeFactory">Opens the scope each load resolves the database context from.</param>
    public SmtpSettingsCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Reads the panel's mail settings, loading them on the first call after an invalidation.</summary>
    /// <param name="cancellationToken">Cancellation token for the load.</param>
    /// <returns>The settings, or <c>null</c> when the panel has none configured.</returns>
    public async Task<SmtpProfile?> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_loaded)
            {
                return _profile;
            }

            // Read and published under the one gate an invalidation must also hold. A save that
            // commits while this query is in flight cannot invalidate until the publish below has
            // happened, and its invalidation then clears exactly the row it superseded.
            _profile = await LoadAsync(cancellationToken);
            _loaded = true;
            return _profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets what is cached, so the next read goes to the database.</summary>
    /// <remarks>
    /// <para>
    /// Called after a save commits, never before: invalidating first would let a read that starts
    /// afterwards load the pre-commit row and cache it as current.
    /// </para>
    /// <para>
    /// Ordering alone does not close the window, and this used to claim it did. A load already in
    /// flight could publish the pre-save profile afterwards, and with no expiry on this cache that
    /// profile is the panel's mail server until the process restarts. Taking the load gate is what
    /// removes the interleaving rather than shrinking it: while a load holds the gate this call
    /// waits, so the clearing below always happens after that load's publish, never before it.
    /// </para>
    /// <para>
    /// The wait is bounded by one settings query and is paid by the save request, which is the right
    /// caller to pay it: mail settings are saved perhaps twice in a server's life, and the blocked
    /// caller is the administrator who asked for the change.
    /// </para>
    /// <para>
    /// The gate is not reentrant, so this is called from a request that holds nothing, never from
    /// inside a load. Nothing on the load path invalidates, and nothing should be made to.
    /// </para>
    /// </remarks>
    public void Invalidate()
    {
        _gate.Wait();

        try
        {
            _loaded = false;
            _profile = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the load gate when the container shuts the panel down.</summary>
    /// <remarks>
    /// Present because the gate is a disposable the type owns, and a singleton the container built is
    /// a singleton the container disposes. Nothing here is reachable afterwards: the cache lives as
    /// long as the process, so this runs once, at the end.
    /// </remarks>
    public void Dispose()
    {
        _gate.Dispose();
    }

    /// <summary>Reads the settings row and projects it onto a detached snapshot.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>The snapshot, or <c>null</c> when the table is empty.</returns>
    /// <remarks>
    /// <c>AsNoTracking</c> because nothing here ever writes through this context, and the row is
    /// about to outlive the scope it was read in.
    /// </remarks>
    private async Task<SmtpProfile?> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var settings = await dbContext.SmtpSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == Domain.Entities.SmtpSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            return null;
        }

        return new SmtpProfile(
            settings.Host,
            settings.Port,
            settings.Security,
            settings.Username,
            settings.Password,
            settings.FromAddress,
            settings.FromName,
            settings.AlertRecipient);
    }
}
