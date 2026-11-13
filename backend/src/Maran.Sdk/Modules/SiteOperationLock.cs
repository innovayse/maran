using Maran.Sdk.Interfaces;

namespace Maran.Sdk.Modules;

/// <summary>
/// The in-process <see cref="ISiteOperationLock"/>: one semaphore per site, created on first use and
/// removed when the last holder or waiter has left, so the table holds only sites in use right now.
/// </summary>
/// <remarks>
/// Registered once as a singleton (<c>AddSdk</c>), so Sites and Ssl share one instance. Every change to
/// the table and to a key's reference count happens under <see cref="_gate"/>; the semaphore itself is
/// awaited outside it, so a long wait on one site never blocks bookkeeping for another.
/// </remarks>
public sealed class SiteOperationLock : ISiteOperationLock
{
    /// <summary>The code of the failure returned when a site stays in use for the whole wait.</summary>
    public const string BusyCode = "SiteBusy";

    /// <summary>The failure returned when the wait ends with the site still in use.</summary>
    public static readonly Error Busy = Error.Of(BusyCode, ErrorType.Conflict);

    /// <summary>Guards <see cref="_entries"/> and every <see cref="Entry.References"/>.</summary>
    private readonly object _gate = new();

    /// <summary>The semaphores of the sites in use, by key.</summary>
    private readonly Dictionary<SiteLockKey, Entry> _entries = new();

    /// <summary>The number of sites that currently have a holder or a waiter; for tests and diagnostics.</summary>
    public int TrackedSiteCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> AcquireAsync(SiteLockKey key, TimeSpan wait, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);

        var entry = Join(key);
        bool entered;
        try
        {
            entered = await entry.Semaphore.WaitAsync(wait, ct);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }

        if (!entered)
        {
            Leave(key, entry);

            return Result<IAsyncDisposable>.Fail(Busy);
        }

        return Result<IAsyncDisposable>.Ok(new Held(this, key, entry));
    }

    /// <summary>Registers one more interested party for a site, creating its entry when it is the first.</summary>
    /// <param name="key">The site, or the account's pools.</param>
    /// <returns>The site's entry.</returns>
    private Entry Join(SiteLockKey key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.References++;

            return entry;
        }
    }

    /// <summary>Unregisters one interested party, removing the entry when it was the last.</summary>
    /// <param name="key">The site, or the account's pools.</param>
    /// <param name="entry">The entry <see cref="Join"/> returned.</param>
    private void Leave(SiteLockKey key, Entry entry)
    {
        lock (_gate)
        {
            entry.References--;
            if (entry.References == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    /// <summary>One site's semaphore and the number of holders and waiters that rely on it.</summary>
    private sealed class Entry
    {
        /// <summary>Admits one holder at a time.</summary>
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Holders plus waiters; read and written only under the lock's gate.</summary>
        public int References { get; set; }
    }

    /// <summary>The handle of a held site; disposing it releases the site once.</summary>
    private sealed class Held : IAsyncDisposable
    {
        /// <summary>The lock the site was taken from.</summary>
        private readonly SiteOperationLock _owner;

        /// <summary>The held key.</summary>
        private readonly SiteLockKey _key;

        /// <summary>The held site's entry.</summary>
        private readonly Entry _entry;

        /// <summary>Set to 1 by the first release, so a second one does nothing.</summary>
        private int _released;

        /// <summary>Creates the handle.</summary>
        /// <param name="owner">The lock the site was taken from.</param>
        /// <param name="key">The held key.</param>
        /// <param name="entry">The held site's entry.</param>
        public Held(SiteOperationLock owner, SiteLockKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _entry.Semaphore.Release();
                _owner.Leave(_key, _entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
