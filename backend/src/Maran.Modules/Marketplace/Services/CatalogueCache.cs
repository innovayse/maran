using System.Collections.Concurrent;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Holds verified catalogue pages in memory for a few minutes so that paging back and forth, or opening
/// the screen twice, does not ask the marketplace twice. Nothing from the catalogue is ever written to
/// the database; a restart empties it.
/// </summary>
/// <remarks>
/// <para>
/// A page is kept per <see cref="CatalogueCacheKey"/>: channel and locale, as the contract names, plus the
/// search term and cursor that make two pages different, and the fingerprint of the lease credential in
/// use, because the entitlement of each module is the service's answer for that lease: when a lease
/// arrives, is replaced or ends, the key changes and the next request asks the service instead of showing
/// the old entitlement for the rest of the time-to-live. Install, update and remove call
/// <see cref="Invalidate"/>, so the screen after an operation shows the new state and not a page that
/// still says "available".
/// </para>
/// <para>
/// An entry past its time-to-live is not served but is kept: its ETag lets the next request ask
/// "has it changed" and take a bodyless 304 for the answer. The caller re-judges the page's freshness
/// every time it is served, because a cache hit must not extend the life of a signature.
/// </para>
/// Registered as a singleton.
/// </remarks>
public sealed class CatalogueCache
{
    /// <summary>The held entries.</summary>
    private readonly ConcurrentDictionary<CatalogueCacheKey, CatalogueCacheEntry> _entries = new();

    /// <summary>The options reader.</summary>
    private readonly MarketplaceOptionsReader _options;

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the cache.</summary>
    /// <param name="options">The options reader, for the time-to-live.</param>
    /// <param name="clock">The time source.</param>
    public CatalogueCache(MarketplaceOptionsReader options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    /// <summary>Builds the key a request is cached under.</summary>
    /// <param name="channel">The release channel.</param>
    /// <param name="locale">The interface language.</param>
    /// <param name="search">The search term, or null.</param>
    /// <param name="cursor">The page cursor, or null.</param>
    /// <param name="lease">The fingerprint of the lease credential in use (<see cref="LeaseFingerprintReader"/>), or empty for none.</param>
    /// <returns>The key, with absent parts spelled as empty text so null and empty are one request.</returns>
    public static CatalogueCacheKey KeyOf(string channel, string locale, string? search, string? cursor, string lease)
    {
        return new CatalogueCacheKey(channel, locale, search ?? string.Empty, cursor ?? string.Empty, lease);
    }

    /// <summary>Finds the entry for a key, fresh or not.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The entry, or null.</returns>
    public CatalogueCacheEntry? Find(CatalogueCacheKey key)
    {
        return _entries.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>Tells whether an entry is still inside its time-to-live.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>True when it may be served without asking the marketplace. False when the options cannot be read.</returns>
    public bool IsFresh(CatalogueCacheEntry entry)
    {
        var settings = _options.Read();
        if (!settings.IsSuccess)
        {
            return false;
        }

        return _clock.UtcNow - entry.StoredAt < TimeSpan.FromMinutes(settings.Value.CacheMinutes);
    }

    /// <summary>Stores a verified page, or refreshes the time-to-live of the one already held.</summary>
    /// <param name="key">The key.</param>
    /// <param name="page">The verified page.</param>
    /// <param name="etag">The ETag the service sent with it, or null.</param>
    public void Store(CatalogueCacheKey key, CataloguePage page, string? etag)
    {
        _entries[key] = new CatalogueCacheEntry(page, etag, _clock.UtcNow);
    }

    /// <summary>Forgets one entry, used when its page turned out stale.</summary>
    /// <param name="key">The key.</param>
    public void Remove(CatalogueCacheKey key)
    {
        _entries.TryRemove(key, out _);
    }

    /// <summary>Forgets every entry. Called after an install, update or remove, and when the marketplace is switched.</summary>
    public void Invalidate()
    {
        _entries.Clear();
    }
}
