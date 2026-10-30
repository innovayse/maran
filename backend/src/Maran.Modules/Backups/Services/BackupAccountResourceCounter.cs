using Maran.Modules.Backups.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// This module's answer to <see cref="IAccountResourceCounter"/>: how many backups an account owns,
/// counted from this module's own table.
/// </summary>
/// <remarks>
/// The count runs through <see cref="BackupsDbContext"/>'s tenant query filter, so a customer's scope can only
/// ever count its own rows and an administrator's scope counts the named account's. No filter is
/// bypassed. The table is the truth here, so the answer is always <see cref="AccountResourceCountState.Counted"/>.
///
/// Every backup the panel holds for the account is counted, the copy taken before a deletion
/// included: the summary reports what is stored, and the plan carries no backup limit to compare it to.
/// </remarks>
public sealed class BackupAccountResourceCounter : IAccountResourceCounter
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>Creates the counter over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public BackupAccountResourceCounter(BackupsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public AccountResourceKind Kind
    {
        get
        {
            return AccountResourceKind.Backups;
        }
    }

    /// <inheritdoc />
    public async Task<AccountResourceCount> CountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var used = await _dbContext.Backups
            .AsNoTracking()
            .CountAsync(row => row.AccountId == accountId, cancellationToken);
        return AccountResourceCount.Counted(AccountResourceKind.Backups, used);
    }
}
