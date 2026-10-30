using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp.Services;

/// <summary>
/// This module's answer to <see cref="IAccountResourceCounter"/>: how many SFTP logins an account owns,
/// counted from this module's own table.
/// </summary>
/// <remarks>
/// The count runs through <see cref="SftpDbContext"/>'s tenant query filter, so a customer's scope can only
/// ever count its own rows and an administrator's scope counts the named account's. No filter is
/// bypassed. The table is the truth here, so the answer is always <see cref="AccountResourceCountState.Counted"/>.
/// </remarks>
public sealed class SftpAccountResourceCounter : IAccountResourceCounter
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the counter over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SftpAccountResourceCounter(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public AccountResourceKind Kind
    {
        get
        {
            return AccountResourceKind.SftpLogins;
        }
    }

    /// <inheritdoc />
    public async Task<AccountResourceCount> CountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var used = await _dbContext.SftpUsers
            .AsNoTracking()
            .CountAsync(row => row.AccountId == accountId, cancellationToken);
        return AccountResourceCount.Counted(AccountResourceKind.SftpLogins, used);
    }
}
