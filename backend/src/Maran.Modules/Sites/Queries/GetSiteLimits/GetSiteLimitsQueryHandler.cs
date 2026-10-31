using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Queries.GetSiteLimits;

/// <summary>Handles <see cref="GetSiteLimitsQuery"/> from the module's own policies.</summary>
/// <remarks>
/// Server-wide, not tenant data: the bound is the same for every account, so nothing is read and
/// there is nothing to scope.
/// </remarks>
public sealed class GetSiteLimitsQueryHandler
{
    /// <summary>Returns the bounds a site's form must respect.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Unused: nothing is read.</param>
    /// <returns>A successful result carrying the limits; this operation never fails.</returns>
    public static Task<Result<SiteLimitsDto>> HandleAsync(GetSiteLimitsQuery query, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<SiteLimitsDto>.Ok(new SiteLimitsDto(SiteAliasLimit.MaximumAliases)));
    }
}
