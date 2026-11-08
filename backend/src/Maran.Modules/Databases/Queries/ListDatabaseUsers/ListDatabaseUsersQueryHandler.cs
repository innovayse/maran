using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Databases.Queries.ListDatabaseUsers;

/// <summary>
/// Handles <see cref="ListDatabaseUsersQuery"/>: one page of users with their grants, the database
/// names those grants are on and whether each user is a database's dedicated one, each looked up once
/// for the whole page and never per row.
/// </summary>
public sealed class ListDatabaseUsersQueryHandler
{
    /// <summary>The Databases module's database context, whose filter scopes every read to the caller's tenant.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Resolves the owning accounts' names.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The window onto account names.</param>
    public ListDatabaseUsersQueryHandler(DatabasesDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Reads the page.</summary>
    /// <param name="query">Page, size, search, sort and the optional account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page of users.</returns>
    public async Task<Result<PagedResult<DatabaseUserDto>>> HandleAsync(
        ListDatabaseUsersQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<DatabaseUser> users = _dbContext.DatabaseUsers.AsNoTracking().Include(user => user.Grants);

        if (query.AccountId is { } accountId)
        {
            users = users.Where(user => user.AccountId == accountId);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards.
            var pattern = LikePattern.Contains(query.Search.Value);
            users = users.Where(user => EF.Functions.ILike(user.FullName, pattern, LikePattern.EscapeCharacter));
        }

        var page = await users.ToPagedAsync(query, ListDatabaseUsersSorts.Map, cancellationToken);

        var ownerIds = page.Items.Select(user => { return user.AccountId; }).Distinct().ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        // One read of the owners' databases answers both questions the rows need: what a grant's
        // database is called, and whether any database names the user as its dedicated one.
        var databases = await _dbContext.Databases
            .AsNoTracking()
            .Where(database => ownerIds.Contains(database.AccountId))
            .Select(database => new { database.Id, database.AccountId, database.FullName, database.DbUserNameSuffix })
            .ToListAsync(cancellationToken);
        var databaseNames = databases.ToDictionary(database =>
        {
            return database.Id;
        }, database =>
        {
            return database.FullName;
        });
        var dedicated = databases
            .Select(database => { return (database.AccountId, database.DbUserNameSuffix); })
            .ToHashSet();

        return Result<PagedResult<DatabaseUserDto>>.Ok(page.Map(user =>
        {
            return DatabaseUserMapper.From(
                user, ownerNames, databaseNames, dedicated.Contains((user.AccountId, user.NameSuffix)));
        }));
    }
}
