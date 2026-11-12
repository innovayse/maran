using Maran.Agent.Client.Interfaces;
using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Interfaces;
using Maran.Modules.Apps.Jobs;
using Maran.Modules.Apps.Persistence;
using Maran.Modules.Apps.Resources;
using Maran.Modules.Apps.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Apps.Commands.InstallApp;

/// <summary>
/// Handles <see cref="InstallAppCommand"/>: checks the request, writes the ledger row and the task, and
/// hands the install to the job, answering before the work is done (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>Order:</b> the id; the ledger row (an install already running is answered with its task and nothing
/// is created; an app already installed and one being removed are refused); the one-web-server rule; the
/// agent's list of what its package sources offer; then the task, the row, the audit entry and the
/// hand-over. Every refusal on the way is journalled with its reason and changes nothing.
/// </para>
/// <para>
/// <b>The checks above are advice; the ledger's guards decide.</b> Two requests can pass every check on the
/// same reading. The row's concurrency token and the unique indexes (one row per app, one active web server)
/// make the save the atomic step, and the request that loses it is answered with a typed conflict and its
/// task closed as failed (issue #526).
/// </para>
/// <para>
/// <b>A repeat is not an error.</b> Two administrators pressing install, or a page left open, ask for a
/// state the panel is already producing; the second request is answered with the first one's task, which is
/// what the first-run screen needs to resume watching after a reload.
/// </para>
/// <para>
/// <b>The web server is exclusive here as well as in the agent.</b> The agent refuses a second site web
/// server with <c>AgentWebServerAlreadyInstalled</c>; the panel refuses first, with the ledger it already
/// holds, so no task is begun for an install that cannot succeed.
/// </para>
/// </remarks>
public sealed class InstallAppCommandHandler
{
    /// <summary>The agent's app store surface.</summary>
    private readonly IAgentAppsClient _agent;

    /// <summary>Records the long-running task an operator watches.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Ties the task to the request that started it.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>The Apps module's database context.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>Hands the accepted install to the worker that runs it.</summary>
    private readonly IAppOperationDispatcher _dispatcher;

    /// <summary>This module's audit journal.</summary>
    private readonly AppsAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent's app store surface.</param>
    /// <param name="tasks">Records the long-running task.</param>
    /// <param name="correlationIds">Ties the task to its request.</param>
    /// <param name="dbContext">The Apps module's database context.</param>
    /// <param name="dispatcher">Hands the accepted install to the worker.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The panel's clock.</param>
    public InstallAppCommandHandler(
        IAgentAppsClient agent,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds,
        AppsDbContext dbContext,
        IAppOperationDispatcher dispatcher,
        AppsAuditJournal journal,
        IClock clock)
    {
        _agent = agent;
        _tasks = tasks;
        _correlationIds = correlationIds;
        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Starts the install.</summary>
    /// <param name="command">The validated command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The task that carries the work, or the coded refusal.</returns>
    public async Task<Result<AppOperationAcceptedDto>> HandleAsync(
        InstallAppCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AppId.TryParse(command.AppId, out var appId))
        {
            return Result<AppOperationAcceptedDto>.Fail(
                Error.Of(nameof(ErrorMessages.AppIdInvalid), ErrorType.Validation));
        }

        var rows = await _dbContext.InstalledApps.ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault(app =>
        {
            return string.Equals(app.AppId, appId.Value, StringComparison.Ordinal);
        });

        if (row is { State: AppState.Installing, TaskId: { } running })
        {
            return Result<AppOperationAcceptedDto>.Ok(new AppOperationAcceptedDto(running));
        }

        var refusal = Refusal(appId, row, rows);
        if (refusal is not null)
        {
            return await RefuseAsync(command, refusal, cancellationToken);
        }

        var installable = await _agent.ListInstallableAppsAsync(cancellationToken);
        if (!installable.IsSuccess)
        {
            return await RefuseAsync(command, installable.Error!, cancellationToken);
        }

        var offered = installable.Value.Apps.Any(app =>
        {
            return string.Equals(app.AppId, appId.Value, StringComparison.Ordinal);
        });
        if (!offered)
        {
            return await RefuseAsync(
                command,
                Error.Of(nameof(ErrorMessages.AppsNotInstallable), ErrorType.Validation),
                cancellationToken);
        }

        // No account and audience AdministratorOnly: an app is installed for the whole server, so the task
        // concerns no account and is no customer's to read.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.AppInstall,
            appId.Value,
            accountId: null,
            TaskAudience.AdministratorOnly,
            _correlationIds.CorrelationId,
            cancellationToken);

        var now = _clock.UtcNow;
        var target = row;
        if (target is null)
        {
            target = InstalledApp.BeginInstall(Guid.NewGuid(), appId, taskId, now);
            _dbContext.InstalledApps.Add(target);
        }
        else
        {
            target.RetryInstall(taskId, now);
        }

        Error? lost = null;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // A guard of the ledger refused the write: the row changed since it was read, the app's row
            // already exists, or another web server holds the ports. Another request won; its task is the
            // one that runs and this one never will. Any other database failure is a fault and propagates.
            lost = AppLedgerConflicts.Classify(exception);
            if (lost is null)
            {
                throw;
            }
        }

        if (lost is not null)
        {
            await _tasks.FailAsync(taskId, lost.Code, CancellationToken.None);

            return await RefuseAsync(command, lost, cancellationToken);
        }

        await _journal.RecordRequestAsync(
            AppsAuditJournal.AppInstallRequested,
            appId.Value,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        try
        {
            await _dispatcher.DispatchAsync(
                new AppOperationRequested(taskId, AppOperation.Install, appId.Value),
                cancellationToken);
        }
        catch
        {
            // The worker will never run this, so the task must not stay open and the row must not say
            // "installing". Rethrown unchanged: an infrastructure fault is not an answer to the administrator.
            await _tasks.FailAsync(taskId, nameof(ErrorMessages.AppsOperationFailed), CancellationToken.None);
            target.MarkFailed(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return Result<AppOperationAcceptedDto>.Ok(new AppOperationAcceptedDto(taskId));
    }

    /// <summary>Decides whether the ledger forbids this install.</summary>
    /// <param name="appId">The app asked for.</param>
    /// <param name="row">The app's own row, or null.</param>
    /// <param name="rows">Every row of the ledger.</param>
    /// <returns>The refusal, or <see langword="null"/> when the install may go ahead.</returns>
    private static Error? Refusal(AppId appId, InstalledApp? row, List<InstalledApp> rows)
    {
        if (row is { State: AppState.Installed })
        {
            return Error.Of(nameof(ErrorMessages.AppsAlreadyInstalled), ErrorType.Conflict);
        }

        if (row is { State: AppState.Removing })
        {
            return Error.Of(nameof(ErrorMessages.AppsOperationInProgress), ErrorType.Conflict);
        }

        if (appId.Kind != AppKind.WebServer)
        {
            return null;
        }

        // One site web server at a time. A rival that is installing or being removed still holds the ports.
        var rival = rows.Any(app =>
        {
            return app.Kind == AppKind.WebServer
                && !string.Equals(app.AppId, appId.Value, StringComparison.Ordinal)
                && app.State is AppState.Installed or AppState.Installing or AppState.Removing;
        });

        return rival
            ? Error.Of(nameof(ErrorMessages.AppsWebServerAlreadyInstalled), ErrorType.Conflict)
            : null;
    }

    /// <summary>Journals a refused request and answers with the refusal.</summary>
    /// <param name="command">The command that was refused.</param>
    /// <param name="error">The refusal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<AppOperationAcceptedDto>> RefuseAsync(
        InstallAppCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordRequestAsync(
            AppsAuditJournal.AppInstallRequested,
            command.AppId,
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result<AppOperationAcceptedDto>.Fail(error);
    }
}
