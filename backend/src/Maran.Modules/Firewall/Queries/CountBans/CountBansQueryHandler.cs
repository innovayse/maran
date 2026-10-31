using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Persistence;

namespace Maran.Modules.Firewall.Queries.CountBans;

/// <summary>
/// Handles <see cref="CountBansQuery"/> with one <c>count(*)</c> over <c>firewall.BanEpisodes</c>.
/// </summary>
/// <remarks>
/// Counts through <see cref="BanEpisode.InForceAt"/>, the same predicate the bans list pages over, so
/// the figure on the landing screen is the total the list reports — and, like the list, it is read
/// from the panel's rows and never from the agent.
/// </remarks>
public sealed class CountBansQueryHandler
{
    /// <summary>The Firewall module's database context, which is the durable store of every ban.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>The panel's clock, which decides what has run out.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public CountBansQueryHandler(FirewallDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Counts the bans in force now.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountBansQuery query, CancellationToken cancellationToken)
    {
        return Result<int>.Ok(await _dbContext.BanEpisodes
            .CountAsync(BanEpisode.InForceAt(_clock.UtcNow), cancellationToken));
    }
}
