using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Queries.ListApiKeys;

/// <summary>Handles <see cref="ListApiKeysQuery"/> by reading one page of <c>identity.ApiKeys</c>.</summary>
/// <remarks>
/// <para>
/// The status filter, the search, the count and the page run in PostgreSQL. A status is DERIVED from
/// the revocation and the expiry (it is never stored), so each filter value is the same condition
/// <c>ApiKey.StatusAt</c> applies: revoked wins over expired, and a key expires at its expiry instant,
/// not after it.
/// </para>
/// <para>
/// The rows are projected through <see cref="ApiKeyMapper"/>, which carries the prefix and the key's
/// limits and use. The digest is not a member of <see cref="ApiKeyDto"/>, so the list cannot leak it.
/// </para>
/// </remarks>
public sealed class ListApiKeysQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock, deciding which keys have expired.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public ListApiKeysQueryHandler(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Filters by status, searches the name and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; reading the list never fails.</returns>
    public async Task<Result<PagedResult<ApiKeyDto>>> HandleAsync(
        ListApiKeysQuery query,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        IQueryable<ApiKey> keys = _dbContext.ApiKeys.AsNoTracking();

        if (!query.Status.IsNone)
        {
            keys = query.Status.Value switch
            {
                ApiKeyStatusFilters.Revoked => keys.Where(key => key.RevokedAt != null),
                ApiKeyStatusFilters.Expired => keys.Where(key => key.RevokedAt == null && key.ExpiresAt != null && key.ExpiresAt <= now),
                _ => keys.Where(key => key.RevokedAt == null && (key.ExpiresAt == null || key.ExpiresAt > now)),
            };
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            keys = keys.Where(key => EF.Functions.ILike(key.Name, pattern, LikePattern.EscapeCharacter));
        }

        var page = await keys.ToPagedAsync(query, ListApiKeysSorts.Map, cancellationToken);

        return Result<PagedResult<ApiKeyDto>>.Ok(page.Map(key =>
        {
            return ApiKeyMapper.From(key, now);
        }));
    }
}
