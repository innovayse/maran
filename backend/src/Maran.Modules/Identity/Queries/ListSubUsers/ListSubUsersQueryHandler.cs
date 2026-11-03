using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Queries.ListSubUsers;

/// <summary>
/// Handles <see cref="ListSubUsersQuery"/>: one page of the members of the caller's account, and of
/// the people it has offered a place and who have not answered yet.
/// </summary>
/// <remarks>
/// <para>
/// The account is the one the caller owns and is acting for (<see cref="SubUserOwnerGuard"/>), never a
/// parameter, so another account's team is not reachable at all. Only members are listed; the owner is
/// not a sub-user of their own account.
/// </para>
/// <para>
/// <b>An open offer is a row, shown as invited.</b> Both kinds are projected to the same
/// <see cref="SubUserRow"/> and joined in one query, so search, sort, the total and the page boundary
/// are computed over the mixed set by the database. An offer row's id is the OFFER's, which is what
/// removing the row cancels, and its name and address both read as what the owner typed: the row
/// discloses nothing the owner did not supply, and the login's real name appears only once the offer
/// is accepted and it becomes a member row. An offer that has expired or been answered is not listed.
/// That a row exists at all still tells the owner the identifier qualified, which the threat note
/// states as a limit.
/// </para>
/// </remarks>
public sealed class ListSubUsersQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>The panel's clock, deciding which offers are still open.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="clock">The panel's clock, deciding which offers are still open.</param>
    public ListSubUsersQueryHandler(IdentityDbContext dbContext, SubUserOwnerGuard guard, IClock clock)
    {
        _dbContext = dbContext;
        _guard = guard;
        _clock = clock;
    }

    /// <summary>Reads the page.</summary>
    /// <param name="query">The page, search and sort asked for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The page with the total across every page, or a typed refusal.</returns>
    public async Task<Result<PagedResult<SubUserDto>>> HandleAsync(
        ListSubUsersQuery query,
        CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return Result<PagedResult<SubUserDto>>.Fail(owned.Error!);
        }

        var accountId = owned.Value;
        var now = _clock.UtcNow;
        var members = from membership in _dbContext.AccountMemberships.AsNoTracking()
                      join user in _dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                      where membership.AccountId == accountId && membership.Role == MembershipRole.Member
                      select new SubUserRow
                      {
                          Id = user.Id,
                          Username = user.Username,
                          Email = user.Email,
                          Lifecycle = user.Lifecycle,
                          Grants = membership.Grants,
                          CreatedAt = user.CreatedAt,
                          LastLoginAt = user.LastLoginAt,
                      };

        // Open offers, in the same shape. A pending offer is an invitation that has not been taken
        // up, and it carries only what the owner typed (see the remarks). Its lifecycle is left null
        // rather than written as a constant, so the union never has to give an enum constant a type
        // of its own; SubUserRow.State says what null means.
        var offers = _dbContext.AccountMembershipOffers
            .AsNoTracking()
            .Where(offer => offer.AccountId == accountId && offer.ResolvedAt == null && offer.ExpiresAt > now)
            .Select(offer => new SubUserRow
            {
                Id = offer.Id,
                Username = offer.Identifier,
                Email = offer.Identifier,
                Lifecycle = null,
                Grants = offer.Grants,
                CreatedAt = offer.CreatedAt,
                LastLoginAt = null,
            });

        var rows = members.Concat(offers);

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards
            // so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            rows = rows.Where(row =>
                EF.Functions.ILike(row.Username, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(row.Email, pattern, LikePattern.EscapeCharacter));
        }

        var page = await rows.ToPagedAsync(query, ListSubUsersSorts.Map, cancellationToken);

        return Result<PagedResult<SubUserDto>>.Ok(page.Map(row =>
        {
            return SubUserMapper.From(row);
        }));
    }
}
