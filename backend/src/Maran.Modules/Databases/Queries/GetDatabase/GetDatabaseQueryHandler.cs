using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;

namespace Maran.Modules.Databases.Queries.GetDatabase;

/// <summary>Handles <see cref="GetDatabaseQuery"/> by reading one row within the caller's tenant scope.</summary>
/// <remarks>
/// Another tenant's database is not found rather than forbidden, and that is not a politeness: 403
/// confirms the id names a real database, which turns this endpoint into an oracle for enumerating
/// other customers' data (rules/testing.md item 3). The distinction is not made by this handler at
/// all — the context's query filter means the row genuinely is not there.
///
/// The answer carries no password, and there is nothing here for it to carry one from: no column
/// holds one. The value was shown once, when the database was created or its password last reset.
/// </remarks>
public sealed class GetDatabaseQueryHandler
{
    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    public GetDatabaseQueryHandler(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the database, or <c>DatabaseNotFound</c>.</summary>
    /// <param name="query">Which database to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The database's view, or <c>DatabaseNotFound</c>.</returns>
    public async Task<Result<DatabaseDto>> HandleAsync(GetDatabaseQuery query, CancellationToken cancellationToken)
    {
        var database = await _dbContext.Databases
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == query.DatabaseId, cancellationToken);

        if (database is null)
        {
            return Result<DatabaseDto>.Fail(Error.Of(nameof(ErrorMessages.DatabaseNotFound)));
        }

        return Result<DatabaseDto>.Ok(new DatabaseDto(
            database.Id,
            database.AccountId,
            database.Name,
            database.FullName,
            database.DbUserName,
            database.CreatedAt));
    }
}
