using System.Text.Json;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Runs the job of one import: creates the account, lets every module import its section, restores the home and
/// the databases, and deletes the account again if any step fails (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>It acts as the administrator who staged the import, and as nobody else.</b> Every run opens a job scope
/// (<see cref="IJobCallerScopes"/>) that rebuilds the administrator from the user table; a demoted, disabled or
/// removed initiator gets no scope, and the job halts with <c>MigrationInitiatorGone</c> before it has created
/// anything. A step run outside a scope sees nothing, because the panel's tenant filters show an anonymous
/// caller nothing.
/// </para>
/// <para>
/// <b>Order is the contract between the importers.</b> The Accounts importer is first and creates the account;
/// databases and the rest follow in ascending order; the restore comes last and supplies every byte inside the
/// home. Importers create only panel rows and agent configuration outside the home, so the restore does not
/// erase what they made.
/// </para>
/// <para>
/// <b>Rollback deletes only the account this job created</b>, through the Accounts module's rollback, which
/// refuses an account that existed before the job started. A failure of the rollback itself ends the import as
/// <see cref="AccountImportState.RolledBackIncomplete"/>: the account remains, the row names it, and the
/// administrator is told rather than shown a success. The issued credentials are discarded on any failure.
/// </para>
/// <para>
/// A message carries only the import's id. A failed import does not throw: its outcome is the row's state, and
/// an exception escaping this method is an infrastructure fault.
/// </para>
/// </remarks>
public sealed class AccountImportOrchestrator
{
    /// <summary>
    /// The one log line for an import that faulted. It names the import and the step and carries the
    /// exception for the operator; the exception's text can quote what an importer saw, which is the
    /// customer's data, so it is logged here and never stored on the row or returned.
    /// </summary>
    private static readonly Action<ILogger, Guid, string, Exception?> LogImportFaulted =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Error,
            new EventId(1, nameof(AccountImportOrchestrator)),
            "Import {ImportId} faulted at stage {Stage}.");

    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>Opens the scope the job acts as the initiating administrator in.</summary>
    private readonly IJobCallerScopes _callers;

    /// <summary>Every module's importer, in the order they run.</summary>
    private readonly List<IAccountMigrationImporter> _importers;

    /// <summary>The Backups module's window, absent when the panel has no Backups module.</summary>
    private readonly IMigrationArtifactService? _artifacts;

    /// <summary>Deletes the account a failed import created.</summary>
    private readonly IAccountMigrationRollback? _rollback;

    /// <summary>Opens and closes the task an administrator watches the job by.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Records the outcome.</summary>
    private readonly ServerMigrationAuditJournal _journal;

    /// <summary>Where an unexpected fault is logged.</summary>
    private readonly ILogger<AccountImportOrchestrator> _logger;

    /// <summary>Creates the orchestrator.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="callers">Opens the scope the job acts as the initiating administrator in.</param>
    /// <param name="importers">Every module's importer.</param>
    /// <param name="artifacts">The Backups module's window; a collection, because an absent module registers nothing.</param>
    /// <param name="rollbacks">Deletes the account a failed import created; a collection for the same reason.</param>
    /// <param name="tasks">Opens and closes the task an administrator watches the job by.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the outcome.</param>
    /// <param name="logger">Where an unexpected fault is logged.</param>
    public AccountImportOrchestrator(
        ServerMigrationDbContext dbContext,
        IJobCallerScopes callers,
        IEnumerable<IAccountMigrationImporter> importers,
        IEnumerable<IMigrationArtifactService> artifacts,
        IEnumerable<IAccountMigrationRollback> rollbacks,
        ITaskRecorder tasks,
        IClock clock,
        ServerMigrationAuditJournal journal,
        ILogger<AccountImportOrchestrator> logger)
    {
        _dbContext = dbContext;
        _callers = callers;
        _importers = importers.OrderBy(importer =>
        {
            return importer.Order;
        }).ToList();
        _artifacts = artifacts.FirstOrDefault();
        _rollback = rollbacks.FirstOrDefault();
        _tasks = tasks;
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Runs one import to its end.</summary>
    /// <param name="importId">The import, which must be running.</param>
    /// <param name="cancellationToken">Cancels the job; a cancelled job rolls back like a failed one.</param>
    /// <returns>Resolves when the import has ended, completed or rolled back.</returns>
    public async Task RunAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await _dbContext.AccountImports.FirstOrDefaultAsync(row => row.Id == importId, cancellationToken);

        if (import is null || import.State != AccountImportState.Running)
        {
            return;
        }

        // One task per run, administrators only, so progress shows in Tasks. It is closed on every path below.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.AccountImport,
            import.SourceAccountName,
            null,
            TaskAudience.AdministratorOnly,
            null,
            cancellationToken);

        using var scope = await _callers.BeginAsync(import.CreatedByUserId, cancellationToken);

        if (scope is null)
        {
            await EndAsync(import, taskId, nameof(ErrorMessages.MigrationInitiatorGone), cancellationToken);

            return;
        }

        try
        {
            var failure = await ImportAsync(import, taskId, cancellationToken);

            if (failure is null)
            {
                import.Complete(_clock.UtcNow);
                await _dbContext.SaveChangesAsync(CancellationToken.None);
                await _tasks.CompleteAsync(taskId, CancellationToken.None);
                await _journal.RecordSuccessAsync(
                    MigrationAuditActions.ImportCompleted, import.SourceAccountName, string.Empty, string.Empty, CancellationToken.None);

                return;
            }

            await RollBackAsync(import, taskId, failure, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogImportFaulted(_logger, import.Id, import.Stage ?? string.Empty, exception);
            await RollBackAsync(import, taskId, nameof(ErrorMessages.MigrationImportFaulted), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await RollBackAsync(import, taskId, nameof(ErrorMessages.MigrationImportFaulted), CancellationToken.None);
        }
    }

    /// <summary>Runs the importers and the restore.</summary>
    /// <param name="import">The running import.</param>
    /// <param name="taskId">The task progress is reported on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>null</c> when everything succeeded, otherwise the code of the first thing that did not.</returns>
    private async Task<string?> ImportAsync(AccountImport import, Guid taskId, CancellationToken cancellationToken)
    {
        var sections = JsonSerializer.Deserialize<List<MigrationSection>>(import.SectionsJson, JsonSerializerOptions.Web) ?? [];
        var target = new MigrationImportTarget(
            null,
            import.SourceAccountName,
            new MigrationImportChoices(import.PlanId!.Value, import.TargetResellerId));
        var secrets = new List<MigrationIssuedSecret>();
        var done = 0;

        foreach (var importer in _importers)
        {
            var section = sections.FirstOrDefault(row =>
            {
                return row.ModuleId == importer.ModuleId;
            });

            if (section is null)
            {
                continue;
            }

            import.AtStage(importer.ModuleId);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _tasks.ReportAsync(taskId, StagePercent(done++, _importers.Count + 1), importer.ModuleId, cancellationToken);

            var imported = await importer.ImportAsync(section, target, cancellationToken);

            if (!imported.IsSuccess)
            {
                return imported.Error!.Code;
            }

            secrets.AddRange(imported.Value!.Secrets);

            if (imported.Value.CreatedAccountId is { } createdId)
            {
                // Recorded before anything else is done, so a failure from here on deletes exactly this
                // account and nothing else.
                import.AccountCreated(createdId);
                await _dbContext.SaveChangesAsync(cancellationToken);
                target = target with { AccountId = createdId };
            }
        }

        if (target.AccountId is not { } accountId)
        {
            return MigrationCodes.SectionInvalid;
        }

        if (_artifacts is null)
        {
            return nameof(ErrorMessages.MigrationBackupsUnavailable);
        }

        import.AtStage("restore");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _tasks.ReportAsync(taskId, StagePercent(done, _importers.Count + 1), "restore", cancellationToken);

        var restored = await _artifacts.RestoreImportedAsync(
            new MigrationImportedArtifact(import.ArtifactBackupId, import.ArtifactSizeBytes, import.ArtifactSha256, import.DatabaseCount),
            accountId,
            import.SourceAccountName,
            cancellationToken);

        if (!restored.IsSuccess)
        {
            return restored.Error!.Code;
        }

        // Held encrypted until the administrator claims them once (decision D3).
        import.HoldIssuedSecrets(
            JsonSerializer.Serialize(
                secrets.Select(secret =>
                {
                    return new { subject = secret.Subject, secret = secret.Secret.Reveal() };
                }),
                JsonSerializerOptions.Web));

        return null;
    }

    /// <summary>Deletes the account the job created, if any, and records how the import ended.</summary>
    /// <param name="import">The failed import.</param>
    /// <param name="taskId">The task to close as failed.</param>
    /// <param name="failureCode">The code of what stopped it.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>Resolves when the row records the outcome.</returns>
    private async Task RollBackAsync(AccountImport import, Guid taskId, string failureCode, CancellationToken cancellationToken)
    {
        if (import.TargetAccountId is not { } accountId)
        {
            await EndAsync(import, taskId, failureCode, cancellationToken);

            return;
        }

        var deleted = _rollback is null
            ? Result.Fail(Error.Of(nameof(ErrorMessages.MigrationRollbackUnavailable), ErrorType.Conflict))
            : await _rollback.DeleteImportedAccountAsync(accountId, import.StartedAt!.Value, cancellationToken);

        if (deleted.IsSuccess)
        {
            await EndAsync(import, taskId, failureCode, cancellationToken);

            return;
        }

        import.RollbackFailed(failureCode, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        await _tasks.FailAsync(taskId, failureCode, CancellationToken.None);
        await _journal.RecordFailureAsync(
            MigrationAuditActions.ImportRollbackIncomplete, import.SourceAccountName, string.Empty, string.Empty, CancellationToken.None);
    }

    /// <summary>Ends an import that left nothing behind.</summary>
    /// <param name="import">The import.</param>
    /// <param name="taskId">The task to close as failed.</param>
    /// <param name="failureCode">The code of what stopped it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves when the row records the outcome.</returns>
    private async Task EndAsync(AccountImport import, Guid taskId, string failureCode, CancellationToken cancellationToken)
    {
        import.RollBack(failureCode, _clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _tasks.FailAsync(taskId, failureCode, CancellationToken.None);
        await _journal.RecordFailureAsync(
            MigrationAuditActions.ImportRolledBack, import.SourceAccountName, string.Empty, string.Empty, cancellationToken);
    }

    /// <summary>The share of the job done when stage number <paramref name="finished"/> of <paramref name="stages"/> begins.</summary>
    /// <param name="finished">How many stages have finished.</param>
    /// <param name="stages">How many stages there are, the restore included.</param>
    /// <returns>A percentage below 100: the task reaches 100 only by completing.</returns>
    private static int StagePercent(int finished, int stages)
    {
        return Math.Min(99, finished * 100 / stages);
    }
}
