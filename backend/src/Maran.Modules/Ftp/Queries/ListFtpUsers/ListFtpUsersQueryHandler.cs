using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Mappers;
using Maran.Modules.Ftp.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Ftp.Queries.ListFtpUsers;

/// <summary>
/// Handles <see cref="ListFtpUsersQuery"/> by reading one page of <c>ftp.FtpUsers</c> within the
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
/// what, and they are the only sound answer.
/// </para>
/// <para>
/// <b>That is also why logins the panel does not own are absent from this answer rather than merged
/// into it.</b> The agent does enumerate every login in an account's jail, the panel's own creations
/// and an operator's alike, and that count reaches an operator — but through the account
/// suspension's attestation, where it is reported as a separate, named quantity that the panel did
/// not create and cannot lock. It is never folded into this list, because a row here is a thing this
/// module can delete and re-credential and an unmanaged login is neither; and it is never counted
/// against the plan limit, because an operator's own <c>useradd</c> must not silently consume a
/// customer's allowance. A handler here may not even reach the agent — none is injected.
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
public sealed class ListFtpUsersQueryHandler
{
    /// <summary>The Ftp module's database context, and this module's tenant boundary.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and the account directory.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names, tenant-scoped.</param>
    public ListFtpUsersQueryHandler(FtpDbContext dbContext, IAccountDirectory accounts)
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
    public async Task<Result<PagedResult<FtpUserDto>>> HandleAsync(
        ListFtpUsersQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<FtpUser> ftpUsers = _dbContext.FtpUsers.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            ftpUsers = ftpUsers.Where(ftpUser => ftpUser.AccountId == accountId);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards
            // so the term matches literally. The full name holds both the account prefix and the
            // customer's own name, so one column answers a search for either.
            var pattern = LikePattern.Contains(query.Search.Value);
            ftpUsers = ftpUsers.Where(ftpUser =>
                EF.Functions.ILike(ftpUser.FullName, pattern, LikePattern.EscapeCharacter));
        }

        var page = await ftpUsers.ToPagedAsync(query, ListFtpUsersSorts.Map, cancellationToken);

        // One batched, tenant-scoped lookup for every owner on the page, never one per row.
        var ownerIds = page.Items
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        return Result<PagedResult<FtpUserDto>>.Ok(page.Map(row =>
        {
            return FtpUserMapper.From(row, ownerNames);
        }));
    }
}
