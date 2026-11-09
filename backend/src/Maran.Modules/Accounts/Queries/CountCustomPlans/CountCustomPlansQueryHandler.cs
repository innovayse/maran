using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Seeders;

namespace Maran.Modules.Accounts.Queries.CountCustomPlans;

/// <summary>
/// Handles <see cref="CountCustomPlansQuery"/> with one <c>count(*)</c> over <c>accounts.Plans</c>
/// that leaves out the seeded plans and the plans of resellers.
/// </summary>
/// <remarks>
/// The seeded plans are recognised by the fixed identities <see cref="PlanSeeder"/> gives them, not
/// by name or by position: an administrator may rename or edit a standard plan, and it is still the
/// plan the installation shipped. The caller gates itself to administrators, as the dashboard does.
/// </remarks>
public sealed class CountCustomPlansQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public CountCustomPlansQueryHandler(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts the host's plans that are not one of the seeded standard plans.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountCustomPlansQuery query, CancellationToken cancellationToken)
    {
        var starter = PlanSeeder.StarterPlanId;
        var business = PlanSeeder.BusinessPlanId;
        var unlimited = PlanSeeder.UnlimitedPlanId;

        var count = await _dbContext.Plans
            .CountAsync(
                plan => plan.ResellerId == null
                    && plan.Id != starter
                    && plan.Id != business
                    && plan.Id != unlimited,
                cancellationToken);

        return Result<int>.Ok(count);
    }
}
