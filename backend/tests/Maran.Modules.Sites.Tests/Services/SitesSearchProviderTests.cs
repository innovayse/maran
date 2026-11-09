using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// The Sites feature gate of the sites search: who is offered sites at all. UNOBSERVED HERE: the match and
/// the tenant scoping through it use <c>ILIKE</c>, which the in-memory provider cannot evaluate; the
/// integration suite holds those.
/// </summary>
public sealed class SitesSearchProviderTests
{
    /// <summary>A reseller is offered no site.</summary>
    [Fact]
    public async Task A_reseller_gets_no_hits_and_asks_no_window()
    {
        var user = FakeCurrentUser.Reseller(Guid.NewGuid());
        var features = new Windows(plan: true, grant: true);

        var hits = await Provider(user, features).SearchAsync(new SearchQuery("example"), CancellationToken.None);

        Assert.Empty(hits);
        Assert.Equal(0, features.Asked);
    }

    /// <summary>A customer whose plan lacks the Sites feature is offered no site, and the grant is not even asked.</summary>
    [Fact]
    public async Task A_customer_whose_plan_lacks_sites_gets_no_hits()
    {
        var user = FakeCurrentUser.Customer(Guid.NewGuid());
        var windows = new Windows(plan: false, grant: true);

        var hits = await Provider(user, windows).SearchAsync(new SearchQuery("example"), CancellationToken.None);

        Assert.Empty(hits);
        Assert.Equal(1, windows.Asked);
    }

    /// <summary>A team member the owner has not granted Sites is offered no site.</summary>
    [Fact]
    public async Task A_member_without_the_grant_gets_no_hits()
    {
        var user = FakeCurrentUser.Customer(Guid.NewGuid());
        var windows = new Windows(plan: true, grant: false);

        var hits = await Provider(user, windows).SearchAsync(new SearchQuery("example"), CancellationToken.None);

        Assert.Empty(hits);
        Assert.Equal(2, windows.Asked);
    }

    /// <summary>Builds the provider over a fresh context for the caller.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="windows">The feature windows.</param>
    /// <returns>The provider.</returns>
    private static SitesSearchProvider Provider(FakeCurrentUser user, Windows windows)
    {
        return new SitesSearchProvider(SitesTestContext.Create(user), user, windows, windows);
    }

    /// <summary>Both feature windows in one double, counting how often they were asked.</summary>
    private sealed class Windows : IAccountFeatureDirectory, IAccountMemberGrants
    {
        /// <summary>What the plan window answers.</summary>
        private readonly bool _plan;

        /// <summary>What the grant window answers.</summary>
        private readonly bool _grant;

        /// <summary>Creates it.</summary>
        /// <param name="plan">Whether the plan includes the feature.</param>
        /// <param name="grant">Whether the person was granted it.</param>
        public Windows(bool plan, bool grant)
        {
            _plan = plan;
            _grant = grant;
        }

        /// <summary>How many questions were asked of either window.</summary>
        public int Asked { get; private set; }

        /// <inheritdoc />
        public Task<bool> IncludesAsync(Guid accountId, AccountFeature feature, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(_plan);
        }

        /// <inheritdoc />
        public Task<bool> PermitsAsync(Guid userId, Guid accountId, AccountFeature feature, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(_grant);
        }
    }
}
