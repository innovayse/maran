using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Commands.DiscardAccountImport;

/// <summary>
/// Handles <see cref="DiscardAccountImportCommand"/>: ends a staged import (issue #127).
/// </summary>
/// <remarks>
/// Only a staged import can be discarded; one that is running, or has ended, is answered as not ready, and its
/// row is left exactly as it was. The artifact the agent holds is a backup-root file that expires with the
/// agent's own reaper; this module has no way to ask the agent to delete it and does not pretend to.
/// </remarks>
public sealed class DiscardAccountImportCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the discard.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Names a stored failure in the caller's language.</summary>
    private readonly AccountImportFailureNames _failureNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the discard.</param>
    /// <param name="failureNames">Names a stored failure in the caller's language.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public DiscardAccountImportCommandHandler(
        ServerMigrationDbContext dbContext,
        IClock clock,
        ServerMigrationAuditJournal journal,
        AccountImportFailureNames failureNames,
        IStringLocalizer<DisplayNames> names)
    {
        _failureNames = failureNames;
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
        _names = names;
    }

    /// <summary>Discards the import.</summary>
    /// <param name="command">The import.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The import, now discarded, or the refusal.</returns>
    public async Task<Result<AccountImportDto>> HandleAsync(
        DiscardAccountImportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var import = await _dbContext.AccountImports.FirstOrDefaultAsync(row => row.Id == command.ImportId, cancellationToken);

        if (import is null)
        {
            return Result<AccountImportDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound));
        }

        if (!import.Discard(_clock.UtcNow))
        {
            return Result<AccountImportDto>.Fail(Error.Of(nameof(ErrorMessages.MigrationImportNotReady), ErrorType.Conflict));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.ImportDiscarded,
            import.SourceAccountName,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<AccountImportDto>.Ok(AccountImportMapper.ToDto(import, _failureNames.Of(import.FailureCode), _names));
    }
}
