using Maran.Modules.Accounts.Common;
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

    /// <summary>Resolves each plan's <see cref="Domain.Plan.DisplayNameKey"/> in the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the module's own database context and its typed resource localizer.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="displayNames">Resolves each plan's display-name key in the current request culture.</param>
    public ListPlansQueryHandler(AccountsDbContext dbContext, IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
    }

    /// <summary>Returns every plan as a <see cref="PlanDto"/>, ordered by disk quota ascending.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the plans; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<PlanDto>>> Handle(ListPlansQuery query, CancellationToken cancellationToken)
    {
        var plans = await _dbContext.Plans
            .AsNoTracking()
            .OrderBy(p => p.DiskQuotaMb)
            .ToListAsync(cancellationToken);

        var dtos = plans
            .Select(p =>
            {
                return new PlanDto(
                                p.Id,
                                _displayNames[p.DisplayNameKey],
                                p.DiskQuotaMb,
                                p.MaxSites,
                                p.MaxDatabases,
                                p.MaxFtpUsers);
            })
            .ToList();

        return Result<IReadOnlyList<PlanDto>>.Ok(dtos);
    }
}
