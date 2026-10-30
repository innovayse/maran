using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Mappers;
using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp.Queries.ListSftpUsers;

/// <summary>
/// Handles <see cref="ListSftpUsersQuery"/> by reading <c>sftp.SftpUsers</c> within the caller's
/// tenant scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>The host's own user database is deliberately not consulted here, and no listing in this module
/// may ever consult it.</b> The host has no notion of a tenant: a login name only looks like it
/// belongs to an account because of the prefix the panel put there, so deciding what to show from
/// <c>/etc/passwd</c> means matching a prefix — and <c>alice_</c> is a prefix of <c>alice_bob</c>'s
/// logins too, because account names may contain the separator. Listing account <c>alice</c> that
/// way discloses account <c>alice_bob</c>'s logins. The panel's rows are the record of who asked for
/// what, and they are the only sound answer. A test asserts that this path leaves the agent
/// untouched — indeed that it CANNOT reach the agent, because no handler here may be handed one.
/// </para>
/// <para>
/// The tenant boundary is not the <c>Where</c> clause on the account here: the context's global
/// query filter supplies it, so this handler could not leak another tenant's rows even if it were
/// rewritten carelessly (spec §8). The clause only narrows that scope to the account the caller
/// asked for, and is ANDed with the filter, so a customer naming another account gets nothing.
/// </para>
/// </remarks>
public sealed class ListSftpUsersQueryHandler
{
    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and the account directory.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names, tenant-scoped.</param>
    public ListSftpUsersQueryHandler(SftpDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Returns the caller's SFTP logins, oldest first, each with its owner's name.</summary>
    /// <param name="query">The list request; its optional account narrows the tenant scope.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the logins; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<SftpUserDto>>> HandleAsync(
        ListSftpUsersQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SftpUsers
            .AsNoTracking()
            .Where(sftpUser => query.AccountId == null || sftpUser.AccountId == query.AccountId)
            .OrderBy(sftpUser => sftpUser.CreatedAt)
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

        IReadOnlyList<SftpUserDto> items = rows
            .Select(row =>
            {
                return SftpUserMapper.From(row, ownerNames);
            })
            .ToList();

        return Result<IReadOnlyList<SftpUserDto>>.Ok(items);
    }
}
