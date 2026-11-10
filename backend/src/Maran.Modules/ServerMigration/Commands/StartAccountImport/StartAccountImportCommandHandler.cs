using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Queries.GetAccountImportReport;
using Maran.Modules.ServerMigration.Resources;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.ServerMigration.Commands.StartAccountImport;

/// <summary>
/// Handles <see cref="StartAccountImportCommand"/>: refuses an import with a conflict, checks the two typed
/// confirmations, marks the import running and queues the job (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal is the backend's.</b> The screen disables its start button while a conflict exists, and that is
/// advice; this handler judges the same dry run again at the moment of the start, so a name that was free when
/// the report was read and taken since is a refusal now, and a client that skips the screen starts nothing a
/// report would have refused.
/// </para>
/// <para>
/// The confirmations are compared ordinally and case-sensitively against the stored import, so a typo is a
/// refusal and not a near miss. The queued message carries the import's id and nothing else.
/// </para>
/// </remarks>
public sealed class StartAccountImportCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The module's display names, in the caller's language.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Judges the import again.</summary>
    private readonly GetAccountImportReportQueryHandler _report;

    /// <summary>The bus the job is queued on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the start.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Names a stored failure in the caller's language.</summary>
    private readonly AccountImportFailureNames _failureNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="report">Judges the import again.</param>
    /// <param name="bus">The bus the job is queued on.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the start.</param>
    /// <param name="failureNames">Names a stored failure in the caller's language.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    public StartAccountImportCommandHandler(
        ServerMigrationDbContext dbContext,
        GetAccountImportReportQueryHandler report,
        IMessageBus bus,
        IClock clock,
        ServerMigrationAuditJournal journal,
        AccountImportFailureNames failureNames,
        IStringLocalizer<DisplayNames> names)
    {
        _failureNames = failureNames;
        _dbContext = dbContext;
        _report = report;
        _bus = bus;
        _clock = clock;
        _journal = journal;
        _names = names;
    }

    /// <summary>Starts the import.</summary>
    /// <param name="command">The administrator's choices and confirmations.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The import, now running, or the refusal.</returns>
    public async Task<Result<AccountImportDto>> HandleAsync(
        StartAccountImportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var import = await _dbContext.AccountImports.FirstOrDefaultAsync(row => row.Id == command.ImportId, cancellationToken);

        if (import is null)
        {
            return await FailAsync(command, command.ImportId.ToString(), Error.Of(nameof(ErrorMessages.MigrationImportNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (import.State != AccountImportState.Staged)
        {
            return await FailAsync(command, import.SourceAccountName, Error.Of(nameof(ErrorMessages.MigrationImportNotReady), ErrorType.Conflict), cancellationToken);
        }

        if (!string.Equals(command.ConfirmAccountName, import.SourceAccountName, StringComparison.Ordinal))
        {
            return await FailAsync(command, import.SourceAccountName, Error.Of(nameof(ErrorMessages.MigrationAccountNameConfirmationMismatch), ErrorType.Conflict), cancellationToken);
        }

        if (!string.Equals(command.ConfirmKeyFingerprint, import.KeyFingerprint, StringComparison.Ordinal))
        {
            return await FailAsync(command, import.SourceAccountName, Error.Of(nameof(ErrorMessages.MigrationFingerprintMismatch), ErrorType.Conflict), cancellationToken);
        }

        var report = await _report.HandleAsync(new GetAccountImportReportQuery(command.PlanId, import.Id), cancellationToken);

        if (!report.IsSuccess)
        {
            return await FailAsync(command, import.SourceAccountName, report.Error!, cancellationToken);
        }

        if (!report.Value!.CanStart)
        {
            return await FailAsync(command, import.SourceAccountName, Error.Of(nameof(ErrorMessages.MigrationImportHasConflicts), ErrorType.Conflict), cancellationToken);
        }

        import.Start(command.PlanId, command.ResellerId, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _journal.RecordSuccessAsync(
            MigrationAuditActions.ImportStarted,
            import.SourceAccountName,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        // Published after the commit: a lost message leaves a running import with no job, which the
        // administrator sees as a stage that never advances and discards or restarts. Durable delivery is not
        // claimed here (rules/csharp.md "A message is published AFTER the commit").
        await _bus.PublishAsync(new AccountImportRequested(import.Id));

        return Result<AccountImportDto>.Ok(AccountImportMapper.ToDto(import, _failureNames.Of(import.FailureCode), _names));
    }

    /// <summary>Journals a refusal and answers it.</summary>
    /// <param name="command">The command.</param>
    /// <param name="subject">What the refusal is about.</param>
    /// <param name="error">The refusal.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<AccountImportDto>> FailAsync(
        StartAccountImportCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            MigrationAuditActions.ImportStarted,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<AccountImportDto>.Fail(error);
    }
}
