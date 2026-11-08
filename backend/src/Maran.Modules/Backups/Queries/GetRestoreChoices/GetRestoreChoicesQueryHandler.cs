using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Queries.GetRestoreChoices;

/// <summary>Handles <see cref="GetRestoreChoicesQuery"/>: names the parts of an account a restore can be narrowed to.</summary>
/// <remarks>
/// Authorised the way every read of a backup is: the row is read through the module's tenant-scoped
/// context, so another customer's backup is simply not found and a guessed id reads as 404. The
/// account is then authorised through its own window before either list is asked for, because both
/// windows answer an empty list for an account the caller may not see and an empty list must not be
/// readable as "this account has nothing".
/// </remarks>
public sealed class GetRestoreChoicesQueryHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The one window onto the owning account.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The one window onto the databases the panel knows the account owns.</summary>
    private readonly IAccountDatabaseDirectory _databases;

    /// <summary>The one window onto the sites the panel knows the account hosts.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The owning account.</param>
    /// <param name="databases">The databases the panel knows the account owns.</param>
    /// <param name="sites">The sites the panel knows the account hosts.</param>
    public GetRestoreChoicesQueryHandler(
        BackupsDbContext dbContext,
        IAccountDirectory accounts,
        IAccountDatabaseDirectory databases,
        ISiteDirectory sites)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _databases = databases;
        _sites = sites;
    }

    /// <summary>Reads the choices.</summary>
    /// <param name="query">The backup the choices are for.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The account's databases and sites, or a not-found failure.</returns>
    public async Task<Result<RestoreChoicesDto>> HandleAsync(
        GetRestoreChoicesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var backup = await _dbContext.Backups
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == query.BackupId, cancellationToken);
        if (backup is null)
        {
            return Result<RestoreChoicesDto>.Fail(Error.Of(nameof(ErrorMessages.BackupNotFound), ErrorType.NotFound));
        }

        // A backup outlives its account (the final copy of a deleted one is kept), and there is no
        // account to restore into then: no choices to offer.
        var account = await _accounts.FindAsync(backup.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<RestoreChoicesDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var databases = await _databases.ListNamesAsync(backup.AccountId, cancellationToken);
        var sites = await _sites.ListDomainsAsync(backup.AccountId, cancellationToken);

        return Result<RestoreChoicesDto>.Ok(new RestoreChoicesDto(
            databases.Order(StringComparer.Ordinal).ToList(),
            sites.Order(StringComparer.Ordinal).ToList()));
    }
}
