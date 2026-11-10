using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>The catalogue cache: what a page is kept under, how long it is served, and when it is forgotten.</summary>
public sealed class CatalogueCacheTests
{
    /// <summary>An empty page to store.</summary>
    /// <param name="world">The test world, for the clock.</param>
    /// <returns>The page.</returns>
    private static CataloguePage PageOf(MarketplaceTestWorld world)
    {
        return new CataloguePage([], world.Clock.UtcNow, world.Clock.UtcNow.AddHours(1));
    }

    /// <summary>A page stored for one channel and locale is found for them and for no other pair.</summary>
    [Fact]
    public void A_page_is_kept_per_channel_and_locale()
    {
        using var world = new MarketplaceTestWorld();
        var page = PageOf(world);

        world.Cache.Store(CatalogueCache.KeyOf("stable", "en", null, null), page, "\"1\"");

        Assert.Same(page, world.Cache.Find(CatalogueCache.KeyOf("stable", "en", null, null))!.Page);
        Assert.Null(world.Cache.Find(CatalogueCache.KeyOf("beta", "en", null, null)));
        Assert.Null(world.Cache.Find(CatalogueCache.KeyOf("stable", "ru", null, null)));
    }

    /// <summary>A different search term or cursor is a different page; an absent one and an empty one are the same request.</summary>
    [Fact]
    public void Search_and_cursor_extend_the_key()
    {
        using var world = new MarketplaceTestWorld();
        world.Cache.Store(CatalogueCache.KeyOf("stable", "en", "mail", null), PageOf(world), null);

        Assert.NotNull(world.Cache.Find(CatalogueCache.KeyOf("stable", "en", "mail", string.Empty)));
        Assert.Null(world.Cache.Find(CatalogueCache.KeyOf("stable", "en", "dns", null)));
        Assert.Null(world.Cache.Find(CatalogueCache.KeyOf("stable", "en", "mail", "page2")));
        Assert.Null(world.Cache.Find(CatalogueCache.KeyOf("stable", "en", null, null)));
    }

    /// <summary>An entry is fresh inside the configured minutes and not after them.</summary>
    [Fact]
    public void An_entry_is_fresh_for_the_configured_minutes_only()
    {
        using var world = new MarketplaceTestWorld();
        var key = CatalogueCache.KeyOf("stable", "en", null, null);
        world.Cache.Store(key, PageOf(world), null);
        var entry = world.Cache.Find(key)!;

        world.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.True(world.Cache.IsFresh(entry));

        world.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(world.Cache.IsFresh(entry));
    }

    /// <summary>Invalidating forgets every entry, and a later store is found again.</summary>
    [Fact]
    public void Invalidation_forgets_every_entry()
    {
        using var world = new MarketplaceTestWorld();
        var first = CatalogueCache.KeyOf("stable", "en", null, null);
        var second = CatalogueCache.KeyOf("stable", "ru", null, null);
        world.Cache.Store(first, PageOf(world), null);
        world.Cache.Store(second, PageOf(world), null);

        world.Cache.Invalidate();

        Assert.Null(world.Cache.Find(first));
        Assert.Null(world.Cache.Find(second));

        world.Cache.Store(first, PageOf(world), null);
        Assert.NotNull(world.Cache.Find(first));
    }

    /// <summary>Removing one entry leaves the others.</summary>
    [Fact]
    public void Removing_one_entry_leaves_the_others()
    {
        using var world = new MarketplaceTestWorld();
        var first = CatalogueCache.KeyOf("stable", "en", null, null);
        var second = CatalogueCache.KeyOf("stable", "ru", null, null);
        world.Cache.Store(first, PageOf(world), null);
        world.Cache.Store(second, PageOf(world), null);

        world.Cache.Remove(first);

        Assert.Null(world.Cache.Find(first));
        Assert.NotNull(world.Cache.Find(second));
    }
}
