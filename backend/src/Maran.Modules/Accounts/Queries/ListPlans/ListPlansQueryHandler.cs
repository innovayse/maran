using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListPlans;

/// <summary>
/// Handles <see cref="ListPlansQuery"/> by reading the plans from <c>accounts.Plans</c>, naming each
/// for the current request culture through <see cref="IStringLocalizer{T}"/> keyed by
/// <see cref="DisplayNames"/>, and answering the requested page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Searched, sorted and paged in memory, through the kernel's one in-memory pager.</b> The name a
/// caller searches and sorts by is the name they read, and for a plan the panel ships that is a
/// resource resolved in the request's culture: no column holds it, so neither an <c>ILIKE</c> nor an
/// <c>ORDER BY</c> could match "Стартовый" against a row storing <c>PlanStarterName</c>. Doing it in
/// SQL would answer a Russian reader in English order and miss their search.
/// </para>
/// <para>
/// The read is bounded by what a plan is: a server has as many plans as its operator sells, a
/// handful, which is the same bound <c>PlanNameDirectory</c> reads the table whole under. The account
/// headcounts arrive in one grouped read rather than a count per plan.
/// </para>
/// </remarks>
public sealed class ListPlansQueryHandler
{
    /// <summary>The text a search looks in: the name the caller reads.</summary>
    private static readonly IReadOnlyList<Func<PlanDto, string>> SearchFields =
    [
        plan =>
        {
            return plan.DisplayName;
        },
    ];

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Resolves each shipped plan's display-name key in the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the module's own database context and its typed resource localizer.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="displayNames">Resolves each plan's display-name key in the current request culture.</param>
    public ListPlansQueryHandler(AccountsDbContext dbContext, IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
    }

    /// <summary>
    /// Names every plan, counts its accounts, then searches, orders and pages the rows as
    /// <see cref="ListPlansSorts.Map"/> and the request say.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<PlanDto>>> HandleAsync(ListPlansQuery query, CancellationToken cancellationToken)
    {
        var plans = await _dbContext.Plans.AsNoTracking().ToListAsync(cancellationToken);

        var accountCounts = await _dbContext.Accounts
            .AsNoTracking()
            .GroupBy(a => a.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(
                g =>
                {
                    return g.PlanId;
                },
                g =>
                {
                    return g.Count;
                },
                cancellationToken);

        var rows = plans.Select(plan =>
        {
            return PlanMapper.ToDto(plan, _displayNames, accountCounts.GetValueOrDefault(plan.Id));
        });

        return Result<PagedResult<PlanDto>>.Ok(rows.ToPaged(query, ListPlansSorts.Map, SearchFields));
    }
}
