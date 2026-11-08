using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// This module's answer to the global search: sites found by any hostname they claim, for an
/// administrator and for a customer whose plan and grants include the Sites feature.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope is the context's query filter, and this class adds none.</b> A customer's context holds only
/// their own account's hostnames (<see cref="SitesDbContext"/>), so another customer's site is not found
/// even by its exact hostname, and the answer is the same empty list a name that does not exist gets. The
/// query contains no <c>IgnoreQueryFilters</c>.
/// </para>
/// <para>
/// <b>It applies the feature gate <c>SitesController</c> applies</b> (<c>RequiresAccountFeature</c>): a
/// customer whose plan does not include Sites, or a team member the owner has not granted it to, gets
/// nothing, because the Sites screens refuse them and the palette must not name what they cannot open.
/// A reseller, an API key, an anonymous caller and a customer with no account get nothing: the controller
/// refuses the first two and the filter shows the rest no row. The gate reads two Sdk windows and can
/// only show a person less than the filter allows.
/// </para>
/// <para>
/// <b>The match is a literal case-insensitive substring</b> (<c>ILIKE</c>, term escaped by
/// <see cref="LikePattern"/>; not unit-testable on the in-memory provider) over the hostnames table, the set a name
/// being taken is decided from (<c>SiteHostname</c>), so an alias finds its site as the primary domain
/// does. One hit per site, labelled with the matched hostname when that is not the primary domain.
/// </para>
/// </remarks>
public sealed class SitesSearchProvider : ISearchProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The caller, read only to apply the Sites feature gate.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Whether the customer's plan sells the Sites feature.</summary>
    private readonly IAccountFeatureDirectory _features;

    /// <summary>Whether the person, as a member of the account, was given the Sites feature.</summary>
    private readonly IAccountMemberGrants _grants;

    /// <inheritdoc />
    public SearchKind Kind
    {
        get { return SearchKind.Site; }
    }

    /// <summary>Creates the provider over this module's context and the two feature windows.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="features">Answers whether the customer's plan includes the Sites feature.</param>
    /// <param name="grants">Answers whether the person was granted the Sites feature.</param>
    public SitesSearchProvider(
        SitesDbContext dbContext,
        ICurrentUser currentUser,
        IAccountFeatureDirectory features,
        IAccountMemberGrants grants)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _features = features;
        _grants = grants;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        if (!await MaySearchSitesAsync(ct))
        {
            return [];
        }

        var pattern = LikePattern.Contains(query.Term);
        var exact = LikePattern.Exactly(query.Term);

        var candidates = await _dbContext.SiteHostnames
            .AsNoTracking()
            .Where(hostname => EF.Functions.ILike(hostname.Name, pattern, LikePattern.EscapeCharacter))
            .OrderBy(hostname => EF.Functions.ILike(hostname.Name, exact, LikePattern.EscapeCharacter) ? 0 : 1)
            .ThenBy(hostname => hostname.Name)
            .Take(SearchQuery.CandidateLimit)
            .Select(hostname => new { hostname.Name, hostname.SiteId, SiteDomain = hostname.Site.Domain })
            .ToListAsync(ct);

        return candidates
            .Select(hostname =>
            {
                return new { Hostname = hostname, Rank = SearchRank.Of(query.Term, hostname.Name) };
            })
            .GroupBy(ranked => { return ranked.Hostname.SiteId; })
            .Select(site =>
            {
                return site
                    .OrderBy(ranked => { return ranked.Rank; })
                    .ThenBy(ranked => { return ranked.Hostname.Name; }, StringComparer.Ordinal)
                    .First();
            })
            .OrderBy(best => { return best.Rank; })
            .ThenBy(best => { return best.Hostname.SiteDomain; }, StringComparer.Ordinal)
            .Take(SearchQuery.HitLimit)
            .Select(best =>
            {
                var hostname = best.Hostname;
                var subtitle = string.Equals(hostname.Name, hostname.SiteDomain, StringComparison.Ordinal)
                    ? string.Empty
                    : hostname.Name;
                return new SearchHit(SearchKind.Site, hostname.SiteId, hostname.SiteDomain, subtitle);
            })
            .ToList();
    }

    /// <summary>
    /// Whether the caller may be offered sites at all: an administrator always; a customer when the plan
    /// sells the Sites feature and the person was given it; nobody else.
    /// </summary>
    /// <param name="ct">Cancels the reads.</param>
    /// <returns><c>true</c> when the query may run.</returns>
    private async Task<bool> MaySearchSitesAsync(CancellationToken ct)
    {
        switch (_currentUser.Caller)
        {
            case AdministratorCaller:
                return true;

            case CustomerCaller customer:
                return await _features.IncludesAsync(customer.AccountId, AccountFeature.Sites, ct)
                    && await _grants.PermitsAsync(customer.UserId, customer.AccountId, AccountFeature.Sites, ct);

            default:
                return false;
        }
    }
}
