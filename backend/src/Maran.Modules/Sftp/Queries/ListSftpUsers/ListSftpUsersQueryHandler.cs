using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Persistence;

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
/// There is no <c>Where</c> clause on the account here, and deliberately not one: the context's
/// global query filter supplies it, so this handler could not leak another tenant's rows even if it
/// were rewritten carelessly (spec §8).
/// </para>
/// </remarks>
public sealed class ListSftpUsersQueryHandler
{
    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    public ListSftpUsersQueryHandler(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the caller's SFTP logins, ordered by creation time.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the logins; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<SftpUserDto>>> HandleAsync(
        ListSftpUsersQuery query,
        CancellationToken cancellationToken)
    {
        var sftpUsers = await _dbContext.SftpUsers
            .AsNoTracking()
            .OrderBy(sftpUser => sftpUser.CreatedAt)
            .Select(sftpUser => new SftpUserDto(
                sftpUser.Id,
                sftpUser.AccountId,
                sftpUser.Name,
                sftpUser.FullName,
                sftpUser.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SftpUserDto>>.Ok(sftpUsers);
    }
}
