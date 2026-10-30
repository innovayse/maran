using Maran.Modules.Databases.Persistence;

namespace Maran.Modules.Databases.Queries.CountDatabases;

/// <summary>Handles <see cref="CountDatabasesQuery"/> with one <c>count(*)</c> over <c>databases.Databases</c>.</summary>
/// <remarks>
/// Scoped by the context's global query filter like every other read of the table, so a customer's
/// count is of their own databases and an administrator's is of every database on the server. Like
/// every read in this module it is answered from the panel's rows, never from the agent.
/// </remarks>
public sealed class CountDatabasesQueryHandler
{
    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    public CountDatabasesQueryHandler(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts the databases in the caller's tenant scope.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountDatabasesQuery query, CancellationToken cancellationToken)
    {
        return Result<int>.Ok(await _dbContext.Databases.CountAsync(cancellationToken));
    }
}
