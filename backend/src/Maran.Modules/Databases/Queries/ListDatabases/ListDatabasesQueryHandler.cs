using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Persistence;

namespace Maran.Modules.Databases.Queries.ListDatabases;

/// <summary>
/// Handles <see cref="ListDatabasesQuery"/> by reading <c>databases.Databases</c> within the
/// caller's tenant scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent's own <c>ListDatabases</c> is deliberately not called here, and no listing in this
/// module may ever call it.</b> The MySQL server has no notion of a tenant: a name only looks like
/// it belongs to an account because of the prefix the panel put there, so deciding what to show from
/// the server's names means matching a prefix — and <c>alice_</c> is a prefix of <c>alice_bob</c>'s
/// names too, because account names may contain the separator. Listing account <c>alice</c> that way
/// discloses account <c>alice_bob</c>'s databases. The panel's rows are the record of who asked for
/// what, and they are the only sound answer. A test asserts that this path leaves the agent
/// untouched.
/// </para>
/// <para>
/// There is no <c>Where</c> clause on the account here, and deliberately not one: the context's
/// global query filter supplies it, so this handler could not leak another tenant's rows even if it
/// were rewritten carelessly (spec §8).
/// </para>
/// </remarks>
public sealed class ListDatabasesQueryHandler
{
    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    public ListDatabasesQueryHandler(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the caller's databases, ordered by creation time.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the databases; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<DatabaseDto>>> HandleAsync(
        ListDatabasesQuery query,
        CancellationToken cancellationToken)
    {
        var databases = await _dbContext.Databases
            .AsNoTracking()
            .OrderBy(database => database.CreatedAt)
            .Select(database => new DatabaseDto(
                database.Id,
                database.AccountId,
                database.Name,
                database.FullName,
                database.DbUserName,
                database.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<DatabaseDto>>.Ok(databases);
    }
}
