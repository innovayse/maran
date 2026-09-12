using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Policies;
using Maran.Modules.Ftp.Persistence;

namespace Maran.Modules.Ftp.Queries.ListFtpUsers;

/// <summary>
/// Handles <see cref="ListFtpUsersQuery"/> by reading <c>ftp.FtpUsers</c> within the caller's tenant
/// scope.
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
/// There is no <c>Where</c> clause on the account here, and deliberately not one: the context's
/// global query filter supplies it, so this handler could not leak another tenant's rows even if it
/// were rewritten carelessly (spec §8).
/// </para>
/// </remarks>
public sealed class ListFtpUsersQueryHandler
{
    /// <summary>The Ftp module's database context, and this module's tenant boundary.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    public ListFtpUsersQueryHandler(FtpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the caller's FTPS logins, ordered by creation time.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the logins; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<FtpUserDto>>> HandleAsync(
        ListFtpUsersQuery query,
        CancellationToken cancellationToken)
    {
        var ftpUsers = await _dbContext.FtpUsers
            .AsNoTracking()
            .OrderBy(ftpUser => ftpUser.CreatedAt)
            .Select(ftpUser => new FtpUserDto(
                ftpUser.Id,
                ftpUser.AccountId,
                ftpUser.Name,
                ftpUser.FullName,
                FtpsProtocolName.Ftps,
                ftpUser.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FtpUserDto>>.Ok(ftpUsers);
    }
}
