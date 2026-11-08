using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases.Queries.GetDatabaseUser;

/// <summary>Handles <see cref="GetDatabaseUserQuery"/>.</summary>
public sealed class GetDatabaseUserQueryHandler
{
    /// <summary>The Databases module's database context, whose filter scopes every read to the caller's tenant.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Resolves the owning account's name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The window onto account names.</param>
    public GetDatabaseUserQueryHandler(DatabasesDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Reads the user.</summary>
    /// <param name="query">Which user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The user, or <c>DatabaseUserNotFound</c>.</returns>
    public async Task<Result<DatabaseUserDetailDto>> HandleAsync(
        GetDatabaseUserQuery query,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.DatabaseUsers
            .AsNoTracking()
            .Include(row => row.Grants)
            .SingleOrDefaultAsync(row => row.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return Result<DatabaseUserDetailDto>.Fail(
                Error.Of(nameof(ErrorMessages.DatabaseUserNotFound), ErrorType.NotFound));
        }

        var databases = await _dbContext.Databases
            .AsNoTracking()
            .Where(database => database.AccountId == user.AccountId)
            .Select(database => new { database.Id, database.FullName, database.DbUserNameSuffix })
            .ToListAsync(cancellationToken);
        var databaseNames = databases.ToDictionary(database =>
        {
            return database.Id;
        }, database =>
        {
            return database.FullName;
        });
        var dedicated = databases.Any(database =>
        {
            return database.DbUserNameSuffix == user.NameSuffix;
        });

        var ownerNames = await _accounts.NamesOfAsync([user.AccountId], cancellationToken);

        return Result<DatabaseUserDetailDto>.Ok(DatabaseUserMapper.Detail(user, ownerNames, databaseNames, dedicated));
    }
}
