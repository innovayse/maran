using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Queries.ListMyMembershipOffers;

/// <summary>
/// Handles <see cref="ListMyMembershipOffersQuery"/>: the caller's own open, unexpired offers.
/// </summary>
/// <remarks>
/// The login is the signed-in caller's, so another person's offers are not reachable at all. An offer
/// past its expiry reads as absent, as it does when accepting. A caller who is an administrator, or a
/// customer left with no account, simply has none (an administrator is never offered one) and gets an
/// empty list, not an error. Only what the offered login needs is returned: the account's name as the
/// offer carries it, the features with their localized names and the expiry, and never the owner's
/// address or login name.
/// </remarks>
public sealed class ListMyMembershipOffersQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The localized names of the features.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated caller.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="displayNames">The localized names of the features.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated caller.</param>
    public ListMyMembershipOffersQueryHandler(
        IdentityDbContext dbContext,
        IStringLocalizer<DisplayNames> displayNames,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Reads the caller's open offers.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The open offers, the soonest to expire first.</returns>
    public async Task<Result<IReadOnlyList<MembershipOfferDto>>> HandleAsync(
        ListMyMembershipOffersQuery query,
        CancellationToken cancellationToken)
    {
        var userId = SignedInCallerPolicy.Require(_currentUser).UserId;
        var now = _clock.UtcNow;

        var offers = await _dbContext.AccountMembershipOffers
            .AsNoTracking()
            .Where(offer => offer.UserId == userId && offer.ResolvedAt == null && offer.ExpiresAt > now)
            .OrderBy(offer => offer.ExpiresAt)
            .ThenBy(offer => offer.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyList<MembershipOfferDto> page = offers
            .Select(offer =>
            {
                return MembershipOfferMapper.From(offer, _displayNames);
            })
            .ToList();

        return Result<IReadOnlyList<MembershipOfferDto>>.Ok(page);
    }
}
