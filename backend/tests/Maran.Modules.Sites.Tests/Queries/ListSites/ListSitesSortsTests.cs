using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.ListSites;

namespace Maran.Modules.Sites.Tests.Queries.ListSites;

/// <summary>Behavioural contract of <see cref="ListSitesSorts"/>'s status order.</summary>
public sealed class ListSitesSortsTests
{
    /// <summary>The compiled status rank, as an in-memory list would apply it.</summary>
    private static readonly Func<Site, int> Rank = ListSitesSorts.StatusRank.Compile();

    /// <summary>A serving site ranks before a suspended one, whatever the stored names spell.</summary>
    [Fact]
    public void A_serving_site_ranks_before_a_suspended_one()
    {
        var enabled = NewSite();
        var disabled = NewSite();
        disabled.Disable();

        Assert.Equal(0, Rank(enabled));
        Assert.Equal(1, Rank(disabled));
    }

    /// <summary>
    /// Every status member has a rank of its own: a member added to the enum fails here until the
    /// rank expression names it, instead of silently sharing the fallback rank.
    /// </summary>
    [Fact]
    public void Every_status_member_is_ranked()
    {
        Assert.Equal([SiteStatus.Enabled, SiteStatus.Disabled], Enum.GetValues<SiteStatus>());
    }

    /// <summary>Builds an enabled PHP site.</summary>
    /// <returns>The site.</returns>
    private static Site NewSite()
    {
        return new Site(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "rank.example.com",
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "/home/acct/sites/rank.example.com",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
