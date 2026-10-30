using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Mappers;
using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Sftp.Queries.ListSftpUsers;

/// <summary>
/// Handles <see cref="ListSftpUsersQuery"/> by reading one page of <c>sftp.SftpUsers</c> within the
/// caller's tenant scope.
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
/// asked for, and is ANDed with the filter, so a customer naming another account gets nothing. The
/// filter applies before the count as well as before the page, so a customer's total never counts
/// another tenant's rows either.
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

    /// <summary>
    /// Narrows to the account, searches the login the host holds, and answers the requested page with
    /// the total that matched and each row's owner name.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<SftpUserDto>>> HandleAsync(
        ListSftpUsersQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<SftpUser> sftpUsers = _dbContext.SftpUsers.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            sftpUsers = sftpUsers.Where(sftpUser => sftpUser.AccountId == accountId);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards
            // so the term matches literally. The full name holds both the account prefix and the
            // customer's own name, so one column answers a search for either.
            var pattern = LikePattern.Contains(query.Search.Value);
            sftpUsers = sftpUsers.Where(sftpUser =>
                EF.Functions.ILike(sftpUser.FullName, pattern, LikePattern.EscapeCharacter));
        }

        var page = await sftpUsers.ToPagedAsync(query, ListSftpUsersSorts.Map, cancellationToken);

        // One batched, tenant-scoped lookup for every owner on the page, never one per row.
        var ownerIds = page.Items
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        return Result<PagedResult<SftpUserDto>>.Ok(page.Map(row =>
        {
            return SftpUserMapper.From(row, ownerNames);
        }));
    }
}
