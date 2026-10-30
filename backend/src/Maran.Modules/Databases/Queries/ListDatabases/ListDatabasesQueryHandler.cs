using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Databases.Queries.ListDatabases;

/// <summary>
/// Handles <see cref="ListDatabasesQuery"/> by reading one page of <c>databases.Databases</c> within
/// the caller's tenant scope.
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
/// query filter supplies it, before the count as well as before the page, so this handler could not
/// leak another tenant's rows — or count them — even if it were rewritten carelessly (spec §8). The
/// clause only narrows that scope to the account the caller asked for, and is ANDed with the filter,
/// so a customer naming another account gets an empty page with a total of zero.
/// </para>
/// <para>
/// The search runs over <see cref="Database.FullName"/> only. The full name already
/// carries the owner's prefix, so a term naming the account finds its databases without this module
/// reading the Accounts module's names.
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

    /// <summary>
    /// Narrows to the requested account, searches the full name, and answers the requested page with
    /// the total that matched and each row's owner name.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<DatabaseDto>>> HandleAsync(
        ListDatabasesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Database> databases = _dbContext.Databases.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            databases = databases.Where(database => database.AccountId == accountId);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            databases = databases.Where(database =>
                EF.Functions.ILike(database.FullName, pattern, LikePattern.EscapeCharacter));
        }

        var page = await databases.ToPagedAsync(query, ListDatabasesSorts.Map, cancellationToken);

        // One batched, tenant-scoped lookup for every owner on the page, never one per row.
        var ownerIds = page.Items
            .Select(database =>
            {
                return database.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        return Result<PagedResult<DatabaseDto>>.Ok(page.Map(database =>
        {
            return DatabaseMapper.From(database, ownerNames);
        }));
    }
}
