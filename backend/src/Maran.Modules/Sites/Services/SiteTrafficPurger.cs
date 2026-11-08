using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Persistence;

namespace Maran.Modules.Sites.Services;

/// <summary>Deletes the day rows older than the panel keeps, so the traffic table does not grow for as long as the panel runs.</summary>
/// <remarks>
/// <see cref="TrafficHistory.KeptDays"/> days are kept, which is as long as the longest window a customer
/// may read. A row is one per site per day and small, so the delete is a single statement and not batched.
/// Nothing is journalled: housekeeping is not an operation an operator watches, and how many rows went is
/// the log's business.
/// </remarks>
public sealed class SiteTrafficPurger
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The injected time source the cut-off is measured against.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the purger.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="clock">The time source the cut-off is measured against.</param>
    public SiteTrafficPurger(SitesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Removes every day row older than <see cref="TrafficHistory.KeptDays"/> days.</summary>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many rows were removed; zero is the ordinary answer.</returns>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).AddDays(-TrafficHistory.KeptDays);

#pragma warning disable RS0030 // unattended housekeeping: no principal, and it removes every account's expired rows
        return await _dbContext.SiteTrafficDays
            .IgnoreQueryFilters()
            .Where(day => day.Date < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
#pragma warning restore RS0030
    }
}
