using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Interfaces;

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
/// The tenant boundary is not the <c>Where</c> clause on the account here: the context's global
/// query filter supplies it, so this handler could not leak another tenant's rows even if it were
/// rewritten carelessly (spec §8). The clause only narrows that scope to the account the caller
/// asked for, and is ANDed with the filter, so a customer naming another account gets nothing.
/// </para>
/// </remarks>
public sealed class ListDatabasesQueryHandler
{
    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and the account directory.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names, tenant-scoped.</param>
    public ListDatabasesQueryHandler(DatabasesDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Returns the caller's databases, oldest first, each with its owner's name.</summary>
    /// <param name="query">The list request; its optional account narrows the tenant scope.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the databases; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<DatabaseDto>>> HandleAsync(
        ListDatabasesQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Databases
            .AsNoTracking()
            .Where(database => query.AccountId == null || database.AccountId == query.AccountId)
            .OrderBy(database => database.CreatedAt)
            .ToListAsync(cancellationToken);

        // One batched, tenant-scoped lookup for every owner in the answer, never one per row.
        var ownerIds = rows
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        IReadOnlyList<DatabaseDto> items = rows
            .Select(row =>
            {
                return DatabaseMapper.From(row, ownerNames);
            })
            .ToList();

        return Result<IReadOnlyList<DatabaseDto>>.Ok(items);
    }
}
