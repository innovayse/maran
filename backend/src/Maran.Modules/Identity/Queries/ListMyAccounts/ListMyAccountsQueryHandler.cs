using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Queries.ListMyAccounts;

/// <summary>
/// Handles <see cref="ListMyAccountsQuery"/> by reading the caller's memberships and naming each
/// account through the Accounts module's names-only window.
/// </summary>
/// <remarks>
/// The ids handed to <see cref="IAccountNameDirectory"/> come from the caller's own membership rows
/// and from nowhere else, which is the condition under which that unscoped window is safe to ask. An
/// account whose name cannot be read (it was deleted a moment ago, and its memberships have not been
/// removed yet) is left out rather than listed with no label. A suspended membership is left out too:
/// the list is the selector's choices, and a suspended account is not one the person can switch to.
/// The list is not paged: it is the
/// number of accounts one person holds, read into a selector, not a table.
/// </remarks>
public sealed class ListMyAccountsQueryHandler
{
    /// <summary>The module's database context, read for the memberships.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Names the accounts the memberships point at.</summary>
    private readonly IAccountNameDirectory _accountNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accountNames">Names the accounts the memberships point at.</param>
    public ListMyAccountsQueryHandler(IdentityDbContext dbContext, IAccountNameDirectory accountNames)
    {
        _dbContext = dbContext;
        _accountNames = accountNames;
    }

    /// <summary>Lists the caller's accounts, ordered by name.</summary>
    /// <param name="query">Whose accounts, and which one the token speaks for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>One row per account the caller holds a membership of and that still exists.</returns>
    public async Task<Result<IReadOnlyList<SwitchableAccountDto>>> HandleAsync(
        ListMyAccountsQuery query,
        CancellationToken cancellationToken)
    {
        var accountIds = await _dbContext.AccountMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == query.UserId && membership.State == MembershipState.Active)
            .Select(membership => membership.AccountId)
            .ToListAsync(cancellationToken);

        var names = await _accountNames.NamesOfAsync(accountIds, cancellationToken);

        IReadOnlyList<SwitchableAccountDto> rows = accountIds
            .Where(id => { return names.ContainsKey(id); })
            .Select(id => { return new SwitchableAccountDto(id, names[id], id == query.ActiveAccountId); })
            .OrderBy(row => { return row.Name; }, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => { return row.AccountId; })
            .ToList();

        return Result<IReadOnlyList<SwitchableAccountDto>>.Ok(rows);
    }
}
