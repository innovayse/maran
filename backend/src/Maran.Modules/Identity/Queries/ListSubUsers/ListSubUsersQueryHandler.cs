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
/// Handles <see cref="ListSubUsersQuery"/>: one page of the members of the caller's account.
/// </summary>
/// <remarks>
/// The account is the one the caller owns and is acting for (<see cref="SubUserOwnerGuard"/>), never a
/// parameter, so another account's team is not reachable at all. Only members are listed; the owner is
/// not a sub-user of their own account.
/// </remarks>
public sealed class ListSubUsersQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    public ListSubUsersQueryHandler(IdentityDbContext dbContext, SubUserOwnerGuard guard)
    {
        _dbContext = dbContext;
        _guard = guard;
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
        var rows = from membership in _dbContext.AccountMemberships.AsNoTracking()
                   join user in _dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                   where membership.AccountId == accountId && membership.Role == MembershipRole.Member
                   select new SubUserRow(
                       user.Id,
                       user.Username,
                       user.Email,
                       user.Lifecycle,
                       membership.Grants,
                       user.CreatedAt,
                       user.LastLoginAt);

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
