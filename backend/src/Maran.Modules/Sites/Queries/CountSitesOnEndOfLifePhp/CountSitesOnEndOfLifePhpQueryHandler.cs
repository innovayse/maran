using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Persistence;

namespace Maran.Modules.Sites.Queries.CountSitesOnEndOfLifePhp;

/// <summary>
/// Handles <see cref="CountSitesOnEndOfLifePhpQuery"/>: one grouped read of <c>sites.Sites</c> by PHP
/// version, then the lifecycle rule applied to each distinct version.
/// </summary>
/// <remarks>
/// <para>
/// Scoped by the context's global query filter like every other read of the table, so a customer's
/// count is of their own sites and an administrator's is of every site on the server.
/// </para>
/// <para>
/// The rule is C#, not SQL, so the database is asked only how many sites run each version (a handful
/// of rows, however many sites there are) and <see cref="PhpLifecycle"/> decides which of those
/// versions are over. A site with no PHP version (a static or proxied site stores an empty one) and a
/// site on a version the table has no row for read as <see cref="PhpSupportStatus.Unknown"/> and are
/// NOT counted: the screen this feeds says "end of life", and a version nobody has a date for is not
/// known to be.
/// </para>
/// </remarks>
public sealed class CountSitesOnEndOfLifePhpQueryHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The panel's clock, which the lifecycle rule is read against.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler with the module's own database context and the panel's clock.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="clock">The panel's clock; the ambient one is never read.</param>
    public CountSitesOnEndOfLifePhpQueryHandler(SitesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Counts the sites in the caller's tenant scope whose PHP version reads end of life today.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(
        CountSitesOnEndOfLifePhpQuery query,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var perVersion = await _dbContext.Sites
            .GroupBy(site => site.PhpVersion)
            .Select(group => new { Version = group.Key, Sites = group.Count() })
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var row in perVersion)
        {
            if (PhpLifecycle.Read(row.Version, today).Status == PhpSupportStatus.EndOfLife)
            {
                count += row.Sites;
            }
        }

        return Result<int>.Ok(count);
    }
}
