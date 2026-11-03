using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListAccounts;

/// <summary>
/// Handles <see cref="ListAccountsQuery"/> by reading one page of <c>accounts.Accounts</c>, each row
/// carrying its owning reseller's name.
/// </summary>
public sealed class ListAccountsQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Names each account's owning reseller, in one read for the page.</summary>
    private readonly ResellerNameDirectory _resellerNames;

    /// <summary>Names the account's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the module's own database context and display names.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="resellerNames">Names each account's owning reseller, in one read for the page.</param>
    /// <param name="displayNames">Names each account's state in the response, in the request's culture.</param>
    public ListAccountsQueryHandler(
        AccountsDbContext dbContext,
        ResellerNameDirectory resellerNames,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _resellerNames = resellerNames;
        _displayNames = displayNames;
    }

    /// <summary>
    /// Filters by plan and status, searches the name and the primary domain, and answers the
    /// requested page with the total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<AccountDto>>> HandleAsync(
        ListAccountsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Account> accounts = _dbContext.Accounts.AsNoTracking();

        if (query.PlanId is { } planId)
        {
            accounts = accounts.Where(account => account.PlanId == planId);
        }

        if (query.Status is { } status)
        {
            accounts = accounts.Where(account => account.Status == status);
        }

        if (query.ResellerId is { } resellerId)
        {
            accounts = accounts.Where(account => account.ResellerId == resellerId);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            accounts = accounts.Where(account =>
                EF.Functions.ILike(account.Name, pattern, LikePattern.EscapeCharacter) || EF.Functions.ILike(account.PrimaryDomain, pattern, LikePattern.EscapeCharacter));
        }

        var page = await accounts.ToPagedAsync(query, ListAccountsSorts.Map, cancellationToken);

        var ownerIds = page.Items
            .Where(account => account.ResellerId is not null)
            .Select(account => account.ResellerId!.Value)
            .ToList();
        var resellerNames = await _resellerNames.NamesOfAsync(ownerIds, cancellationToken);

        return Result<PagedResult<AccountDto>>.Ok(page.Map(account =>
        {
            return AccountMapper.ToDto(
                account,
                _displayNames,
                account.ResellerId is { } owner ? resellerNames.GetValueOrDefault(owner) : null);
        }));
    }
}
