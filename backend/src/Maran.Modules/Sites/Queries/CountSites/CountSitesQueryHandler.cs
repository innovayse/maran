using Maran.Modules.Sites.Persistence;

namespace Maran.Modules.Sites.Queries.CountSites;

/// <summary>Handles <see cref="CountSitesQuery"/> with one <c>count(*)</c> over <c>sites.Sites</c>.</summary>
/// <remarks>
/// Scoped by the context's global query filter like every other read of the table, so a customer's
/// count is of their own sites and an administrator's is of every site on the server.
/// </remarks>
public sealed class CountSitesQueryHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    public CountSitesQueryHandler(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts the sites in the caller's tenant scope.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountSitesQuery query, CancellationToken cancellationToken)
    {
        return Result<int>.Ok(await _dbContext.Sites.CountAsync(cancellationToken));
    }
}
