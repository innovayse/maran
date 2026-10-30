using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Sdk.Interfaces;

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

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and the account directory.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names, tenant-scoped.</param>
    public GetDatabaseQueryHandler(DatabasesDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Returns the database labelled with its owner's name, or <c>DatabaseNotFound</c>.</summary>
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
            return Result<DatabaseDto>.Fail(Error.Of(nameof(ErrorMessages.DatabaseNotFound), ErrorType.NotFound));
        }

        var ownerNames = await _accounts.NamesOfAsync([database.AccountId], cancellationToken);

        return Result<DatabaseDto>.Ok(DatabaseMapper.From(database, ownerNames));
    }
}
