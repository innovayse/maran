using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.SharedKernel.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListPlanChoices;

/// <summary>
/// Handles <see cref="ListPlanChoicesQuery"/> by naming every plan in the request's culture and
/// answering one page of them.
/// </summary>
/// <remarks>
/// Searched, sorted and paged in memory for the reason <c>ListPlansQueryHandler</c> gives: a shipped
/// plan's name is a resource resolved per culture, which no column holds. The read is bounded by
/// what a plan is — a server sells a handful — and, unlike the plans list, counts no accounts.
/// </remarks>
public sealed class ListPlanChoicesQueryHandler
{
    /// <summary>The text a search looks in: the name the caller reads.</summary>
    private static readonly IReadOnlyList<Func<PlanChoiceDto, string>> SearchFields =
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
    public ListPlanChoicesQueryHandler(AccountsDbContext dbContext, IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
    }

    /// <summary>Searches the plan names and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated picker request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<PlanChoiceDto>>> HandleAsync(
        ListPlanChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var plans = await _dbContext.Plans.AsNoTracking().ToListAsync(cancellationToken);

        var rows = plans.Select(plan =>
        {
            return PlanMapper.ToChoiceDto(plan, _displayNames);
        });

        return Result<PagedResult<PlanChoiceDto>>.Ok(rows.ToPaged(query, ListPlanChoicesSorts.Map, SearchFields));
    }
}
