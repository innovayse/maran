using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Measures what resellers own, from the rows, for the screens that show usage beside an envelope.
/// </summary>
/// <remarks>
/// It reads through the request's own <see cref="AccountsDbContext"/>, so a reseller measuring itself
/// is narrowed by the context's own filters like every other read of this module; the usage of a
/// reseller the caller cannot see is zero, which is never shown because the reseller itself is not
/// found first. It only reads: the figures an allocation decision is made on are measured inside that
/// decision's own transaction, never taken from here.
/// </remarks>
public sealed class ResellerUsageReader
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public ResellerUsageReader(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Measures one reseller.</summary>
    /// <param name="resellerId">The reseller to measure.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>What the reseller owns; all zero when it owns nothing.</returns>
    public async Task<ResellerUsage> ReadAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        var usage = await ReadManyAsync([resellerId], cancellationToken);
        return usage.GetValueOrDefault(resellerId, ResellerUsage.None);
    }

    /// <summary>Measures several resellers with three queries, however many there are.</summary>
    /// <param name="resellerIds">The resellers to measure, typically the ids on one page of a list.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>One entry per reseller that owns something; a reseller owning nothing has none.</returns>
    public async Task<IReadOnlyDictionary<Guid, ResellerUsage>> ReadManyAsync(
        IReadOnlyCollection<Guid> resellerIds,
        CancellationToken cancellationToken)
    {
        if (resellerIds.Count == 0)
        {
            return new Dictionary<Guid, ResellerUsage>();
        }

        // Expression lambdas, not the braced form used elsewhere in this repository: EF Core translates
        // these into SQL, and a statement-bodied lambda cannot become an expression tree. The captured
        // local is sent as a parameter.
        var ids = resellerIds.ToList();

        var accountCounts = await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.ResellerId != null && ids.Contains(account.ResellerId.Value))
            .GroupBy(account => account.ResellerId!.Value)
            .Select(group => new { ResellerId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var planCounts = await _dbContext.Plans
            .AsNoTracking()
            .Where(plan => plan.ResellerId != null && ids.Contains(plan.ResellerId.Value))
            .GroupBy(plan => plan.ResellerId!.Value)
            .Select(group => new { ResellerId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // The disk the accounts are promised is their plan's quota, summed as a long: a few accounts on
        // a plan near the int ceiling already overflow an int.
        var diskAllocated = await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.ResellerId != null && ids.Contains(account.ResellerId.Value))
            .Join(
                _dbContext.Plans.AsNoTracking(),
                account => account.PlanId,
                plan => plan.Id,
                (account, plan) => new { ResellerId = account.ResellerId!.Value, plan.DiskQuotaMb })
            .GroupBy(row => row.ResellerId)
            .Select(group => new { ResellerId = group.Key, DiskMb = group.Sum(row => (long)row.DiskQuotaMb) })
            .ToListAsync(cancellationToken);

        // The selectors below are delegates run after the reads, not translated, so they are braced.
        var accountsById = accountCounts.ToDictionary(
            row =>
            {
                return row.ResellerId;
            },
            row =>
            {
                return row.Count;
            });
        var plansById = planCounts.ToDictionary(
            row =>
            {
                return row.ResellerId;
            },
            row =>
            {
                return row.Count;
            });
        var diskById = diskAllocated.ToDictionary(
            row =>
            {
                return row.ResellerId;
            },
            row =>
            {
                return row.DiskMb;
            });

        return ids
            .Where(id =>
            {
                return accountsById.ContainsKey(id) || plansById.ContainsKey(id);
            })
            .ToDictionary(
                id =>
                {
                    return id;
                },
                id =>
                {
                    return new ResellerUsage(
                        accountsById.GetValueOrDefault(id),
                        plansById.GetValueOrDefault(id),
                        diskById.GetValueOrDefault(id));
                });
    }
}
