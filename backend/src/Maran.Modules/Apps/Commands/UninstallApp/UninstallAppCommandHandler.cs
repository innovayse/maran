using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Interfaces;
using Maran.Modules.Apps.Jobs;
using Maran.Modules.Apps.Persistence;
using Maran.Modules.Apps.Resources;
using Maran.Modules.Apps.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Apps.Commands.UninstallApp;

/// <summary>
/// Handles <see cref="UninstallAppCommand"/>: checks the request, refuses while a site uses the app,
/// writes the ledger row and the task, and hands the removal to the job, answering before the work is done
/// (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>Order:</b> the id; the ledger row (an app the ledger does not hold is <c>AppsNotInstalled</c>; a
/// removal already running is answered with its task); the sites that use the app (<c>AppsInUse</c>, 409,
/// with the list published under <c>sites</c>); then the task, the row, the audit entry and the hand-over.
/// </para>
/// <para>
/// <b>The panel checks the sites first and the agent checks the disk second.</b> The refusal here names
/// the sites, which is what the administrator needs to act. The agent's own check
/// (<c>AgentAppInUse</c>) stays as the second line for a panel whose rows disagree with the host, so a
/// removal can never take a runtime out from under a live site because a row was stale.
/// </para>
/// <para>
/// <b>A failed install can be removed.</b> A half-installed app is not in use by any site, and removing
/// it is how an administrator clears the failure.
/// </para>
/// </remarks>
public sealed class UninstallAppCommandHandler
{
    /// <summary>Records the long-running task an operator watches.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Ties the task to the request that started it.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>The Apps module's database context.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>Asks which sites use an app.</summary>
    private readonly AppUsageGuard _usage;

    /// <summary>Hands the accepted removal to the worker that runs it.</summary>
    private readonly IAppOperationDispatcher _dispatcher;

    /// <summary>This module's audit journal.</summary>
    private readonly AppsAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="tasks">Records the long-running task.</param>
    /// <param name="correlationIds">Ties the task to its request.</param>
    /// <param name="dbContext">The Apps module's database context.</param>
    /// <param name="usage">Asks which sites use an app.</param>
    /// <param name="dispatcher">Hands the accepted removal to the worker.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The panel's clock.</param>
    public UninstallAppCommandHandler(
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        AppsDbContext dbContext,
        AppUsageGuard usage,
        IAppOperationDispatcher dispatcher,
        AppsAuditJournal journal,
        IClock clock)
    {
        _tasks = tasks;
        _correlationIds = correlationIds;
        _dbContext = dbContext;
        _usage = usage;
        _dispatcher = dispatcher;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Starts the removal.</summary>
    /// <param name="command">The validated command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The task that carries the work, or the coded refusal.</returns>
    public async Task<Result<AppOperationAcceptedDto>> HandleAsync(
        UninstallAppCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AppId.TryParse(command.AppId, out var appId))
        {
            return Result<AppOperationAcceptedDto>.Fail(
                Error.Of(nameof(ErrorMessages.AppIdInvalid), ErrorType.Validation));
        }

        var row = await _dbContext.InstalledApps.FirstOrDefaultAsync(
            app => app.AppId == appId.Value,
            cancellationToken);

        if (row is null || row.State == AppState.Installing)
        {
            return await RefuseAsync(
                command,
                Error.Of(nameof(ErrorMessages.AppsNotInstalled), ErrorType.Conflict),
                extension: null,
                cancellationToken);
        }

        if (row is { State: AppState.Removing, TaskId: { } running })
        {
            return Result<AppOperationAcceptedDto>.Ok(new AppOperationAcceptedDto(running));
        }

        var users = await _usage.FindUsersAsync(appId, cancellationToken);
        if (users is not null)
        {
            return await RefuseAsync(
                command,
                Error.Of(nameof(ErrorMessages.AppsInUse), ErrorType.Conflict),
                ProblemExtension.Of("sites", users),
                cancellationToken);
        }

        var taskId = await _tasks.BeginAsync(
            TaskKinds.AppUninstall,
            appId.Value,
            accountId: null,
            TaskAudience.AdministratorOnly,
            _correlationIds.CorrelationId,
            cancellationToken);

        row.BeginRemoval(taskId, _clock.UtcNow);
        Error? lost = null;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The row changed since it was read: another request began an operation on it first. Its task
            // is the one that runs and this one never will. Any other database failure propagates.
            lost = AppLedgerConflicts.Classify(exception);
            if (lost is null)
            {
                throw;
            }
        }

        if (lost is not null)
        {
            await _tasks.FailAsync(taskId, lost.Code, CancellationToken.None);

            return await RefuseAsync(command, lost, extension: null, cancellationToken);
        }

        await _journal.RecordRequestAsync(
            AppsAuditJournal.AppUninstallRequested,
            appId.Value,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        try
        {
            await _dispatcher.DispatchAsync(
                new AppOperationRequested(taskId, AppOperation.Uninstall, appId.Value),
                cancellationToken);
        }
        catch
        {
            // The worker will never run this, so the task must not stay open and the row must not say
            // "removing". Rethrown unchanged: an infrastructure fault is not an answer to the administrator.
            await _tasks.FailAsync(taskId, nameof(ErrorMessages.AppsOperationFailed), CancellationToken.None);
            row.MarkFailed(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return Result<AppOperationAcceptedDto>.Ok(new AppOperationAcceptedDto(taskId));
    }

    /// <summary>Journals a refused request and answers with the refusal.</summary>
    /// <param name="command">The command that was refused.</param>
    /// <param name="error">The refusal.</param>
    /// <param name="extension">The typed facts published beside the refusal, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<AppOperationAcceptedDto>> RefuseAsync(
        UninstallAppCommand command,
        Error error,
        ProblemExtension? extension,
        CancellationToken cancellationToken)
    {
        await _journal.RecordRequestAsync(
            AppsAuditJournal.AppUninstallRequested,
            command.AppId,
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return extension is null
            ? Result<AppOperationAcceptedDto>.Fail(error)
            : Result<AppOperationAcceptedDto>.Fail(error, extension);
    }
}
