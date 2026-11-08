using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Queries.GetMyBackupSchedule;

/// <summary>
/// Handles <see cref="GetMyBackupScheduleQuery"/> by reading the caller's own row of
/// <c>backups.BackupSchedules</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>An account that is not the caller's own is answered as one that does not exist.</b> The tenant
/// directory shows a customer their own account and nothing else, so an administrator or an
/// account-less login gets 404 here rather than the host-wide policy, which the module's query filter
/// would otherwise show a caller with no account.
/// </para>
/// <para>
/// A caller who has never configured a schedule is answered with an empty scope, not a failure and not
/// an invented default (issue #211): "nothing is scheduled" is the ordinary first state of the screen.
/// </para>
/// </remarks>
public sealed class GetMyBackupScheduleQueryHandler
{
    /// <summary>The Backups module's database context, and this module's tenant boundary.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The one window onto whether the caller's account exists.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="accounts">The window onto whether the caller's account exists.</param>
    public GetMyBackupScheduleQueryHandler(BackupsDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>Returns what is recorded for the caller's account.</summary>
    /// <param name="query">The request, carrying the caller's own account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The scope's schedule, an empty scope when none is saved, or <c>AccountNotFound</c>.</returns>
    public async Task<Result<BackupScheduleScopeDto>> HandleAsync(
        GetMyBackupScheduleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var account = await _accounts.FindAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<BackupScheduleScopeDto>.Fail(
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var schedule = await _dbContext.BackupSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.AccountId == account.Id, cancellationToken);

        return Result<BackupScheduleScopeDto>.Ok(
            new BackupScheduleScopeDto(schedule is null ? null : BackupScheduleMapper.From(schedule)));
    }
}
