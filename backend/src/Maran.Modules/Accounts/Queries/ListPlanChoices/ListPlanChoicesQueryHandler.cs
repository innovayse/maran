using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
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
/// It offers one owner's plans (<see cref="ListPlanChoicesQuery.ForResellerId"/>) because an account
/// is sold only from its owner's plans, so a picker that listed every owner's would offer choices the
/// account's creation refuses.
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

    /// <summary>Decides which owner's plans the caller is offered.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Resolves each shipped plan's display-name key in the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the module's own database context and its typed resource localizer.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">Decides which owner's plans the caller is offered.</param>
    /// <param name="displayNames">Resolves each plan's display-name key in the current request culture.</param>
    public ListPlanChoicesQueryHandler(
        AccountsDbContext dbContext,
        ResellerScope scope,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _scope = scope;
        _displayNames = displayNames;
    }

    /// <summary>Searches the plan names and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated picker request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// A successful result carrying the page, or <c>ResellerNotFound</c> when a reseller asks for another
    /// owner's plans.
    /// </returns>
    public async Task<Result<PagedResult<PlanChoiceDto>>> HandleAsync(
        ListPlanChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var owner = _scope.ResolveOwner(query.ForResellerId);
        if (!owner.IsSuccess)
        {
            return Result<PagedResult<PlanChoiceDto>>.Fail(owner.Error!);
        }

        var ownerId = owner.Value;
        var plans = await _dbContext.Plans
            .AsNoTracking()
            .Where(plan => plan.ResellerId == ownerId)
            .ToListAsync(cancellationToken);

        var rows = plans.Select(plan =>
        {
            return PlanMapper.ToChoiceDto(plan, _displayNames);
        });

        return Result<PagedResult<PlanChoiceDto>>.Ok(rows.ToPaged(query, ListPlanChoicesSorts.Map, SearchFields));
    }
}
