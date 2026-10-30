using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.GetPlan;

/// <summary>
/// Handles <see cref="GetPlanQuery"/> by reading one row of <c>accounts.Plans</c> and counting the
/// accounts on it, the plan named for the request's culture by <see cref="PlanMapper"/> exactly as the
/// plan list names it.
/// </summary>
public sealed class GetPlanQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Resolves a shipped plan's display-name key in the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the module's own database context and its typed resource localizer.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="displayNames">Resolves a shipped plan's display-name key in the current request culture.</param>
    public GetPlanQueryHandler(AccountsDbContext dbContext, IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
    }

    /// <summary>Reads the plan and its headcount.</summary>
    /// <param name="query">The plan to read.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The plan, or <c>PlanNotFound</c> when no plan has that id.</returns>
    public async Task<Result<PlanDto>> HandleAsync(GetPlanQuery query, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.Plans
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<PlanDto>.Fail(Error.Of(nameof(ErrorMessages.PlanNotFound), ErrorType.NotFound));
        }

        var accountCount = await _dbContext.Accounts.CountAsync(a => a.PlanId == plan.Id, cancellationToken);

        return Result<PlanDto>.Ok(PlanMapper.ToDto(plan, _displayNames, accountCount));
    }
}
