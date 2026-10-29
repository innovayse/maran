using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListPlans;

/// <summary>
/// Handles <see cref="ListPlansQuery"/> by reading every plan from <c>accounts.Plans</c> and
/// resolving each one's display name for the current request culture through
/// <see cref="IStringLocalizer{T}"/> keyed by <see cref="DisplayNames"/> (rules/csharp.md
/// "Resources are reached through IStringLocalizer&lt;T&gt;"; rules/architecture.md "The backend
/// owns the data, the SPA renders it").
/// </summary>
public sealed class ListPlansQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Resolves each plan's <see cref="Plan.DisplayNameKey"/> in the current request culture.</summary>
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
    /// Returns every plan as a <see cref="PlanDto"/>, ordered by disk quota ascending and then by id.
    /// The second key is not decoration: two plans may sell the same disk, and without it the order
    /// of such a pair is whichever the database happens to return, which makes a screen of plans
    /// reshuffle itself between two reads of unchanged data.
    /// </summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the plans; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<PlanDto>>> HandleAsync(ListPlansQuery query, CancellationToken cancellationToken)
    {
        var plans = await _dbContext.Plans
            .AsNoTracking()
            .OrderBy(p => p.DiskQuotaMb)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        // One grouped read for every plan's headcount rather than a count per plan.
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

        var dtos = plans
            .Select(p =>
            {
                return PlanMapper.ToDto(p, _displayNames, accountCounts.GetValueOrDefault(p.Id));
            })
            .ToList();

        return Result<IReadOnlyList<PlanDto>>.Ok(dtos);
    }
}
