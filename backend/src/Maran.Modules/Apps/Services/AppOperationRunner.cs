using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Jobs;
using Maran.Modules.Apps.Persistence;
using Maran.Modules.Apps.Resources;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Carries an accepted install or removal to its end: watches the agent's stream, reports its progress onto
/// the task, settles the ledger row and writes the outcome to the journal (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>The terminal event decides, never "the stream ended".</b> A stream that stops without a terminal
/// event has told the panel nothing about whether the packages landed, and reporting success on its
/// silence is the mistake the backup runner's remarks warn about. That case fails the task with
/// <c>AppsOutcomeUnobserved</c> and marks the row failed, which says what happened rather than guessing.
/// </para>
/// <para>
/// <b>The ledger settles before the task closes.</b> The first-run screen leaves when the task is
/// complete and then asks for the stack, so a task that closed first would leave a window in which the
/// stack still said "not ready".
/// </para>
/// <para>
/// <b>A removal that fails asks the host what is true.</b> The agent refusing a removal changed nothing,
/// but a removal that died half-way did, and the row must say what the host has: installed, gone, or (when
/// the host cannot be asked) failed.
/// </para>
/// <para>
/// <b>Shutdown is not a failure.</b> A cancelled run is left exactly as it stands: the start-up passes of
/// the Tasks and Apps modules close the task and settle the row against the host on the next boot.
/// </para>
/// </remarks>
public sealed class AppOperationRunner
{
    /// <summary>The failure of an operation whose outcome the stream never stated: a fault on the panel's side of the conversation.</summary>
    private static readonly Error OutcomeUnobserved =
        Error.Of(nameof(ErrorMessages.AppsOutcomeUnobserved), ErrorType.Failure);

    /// <summary>The failure of an operation that stopped on an unexpected exception.</summary>
    private static readonly Error OperationFailed =
        Error.Of(nameof(ErrorMessages.AppsOperationFailed), ErrorType.Failure);

    /// <summary>Pre-compiled log delegate for an operation that stopped on an exception.</summary>
    private static readonly Action<ILogger, string, AppOperation, Exception?> LogOperationFaulted =
        LoggerMessage.Define<string, AppOperation>(
            LogLevel.Error,
            new EventId(1, nameof(AppOperationRunner)),
            "The {Operation} of app {AppId} stopped on an unexpected exception");

    /// <summary>Pre-compiled log delegate for a ledger row that was missing when the run settled.</summary>
    private static readonly Action<ILogger, string, Exception?> LogRowMissing =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(AppOperationRunner)),
            "App {AppId} had no ledger row when its operation settled");

    /// <summary>The agent's app store surface.</summary>
    private readonly IAgentAppsClient _agent;

    /// <summary>Records the task an operator watches.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The Apps module's database context.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly AppsAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where an unexpected exception is reported.</summary>
    private readonly ILogger<AppOperationRunner> _logger;

    /// <summary>Creates the runner.</summary>
    /// <param name="agent">The agent's app store surface.</param>
    /// <param name="tasks">Records the task.</param>
    /// <param name="dbContext">The Apps module's database context.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="logger">Where an unexpected exception is reported.</param>
    public AppOperationRunner(
        IAgentAppsClient agent,
        ITaskRecorder tasks,
        AppsDbContext dbContext,
        AppsAuditJournal journal,
        IClock clock,
        ILogger<AppOperationRunner> logger)
    {
        _agent = agent;
        _tasks = tasks;
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one accepted operation to its end.</summary>
    /// <param name="message">The accepted operation.</param>
    /// <param name="cancellationToken">Cancellation token; the host's shutdown token.</param>
    /// <returns>A task that completes when the operation has ended and its outcome is recorded.</returns>
    public async Task RunAsync(AppOperationRequested message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        Error? failure;
        try
        {
            failure = await ExecuteAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogOperationFaulted(_logger, message.AppId, message.Operation, exception);
            failure = OperationFailed;
            await SettleFailureAsync(message, CancellationToken.None);
        }

        var action = message.Operation == AppOperation.Install
            ? AppsAuditJournal.AppInstalled
            : AppsAuditJournal.AppUninstalled;

        await _journal.RecordSystemAsync(action, message.AppId, failure is null, CancellationToken.None);

        if (failure is null)
        {
            await _tasks.CompleteAsync(message.TaskId, CancellationToken.None);
            return;
        }

        await _tasks.FailAsync(message.TaskId, failure.Code, CancellationToken.None);
    }

    /// <summary>Watches the stream of one operation and settles the ledger.</summary>
    /// <param name="message">The accepted operation.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>Null when the operation reached the state it was asked for; otherwise the failure.</returns>
    private async Task<Error?> ExecuteAsync(AppOperationRequested message, CancellationToken cancellationToken)
    {
        var events = message.Operation == AppOperation.Install
            ? _agent.InstallAppAsync(message.AppId, cancellationToken)
            : _agent.UninstallAppAsync(message.AppId, cancellationToken);

        var failure = await WatchAsync(events, message.TaskId, cancellationToken);

        if (message.Operation == AppOperation.Install)
        {
            await SettleInstallAsync(message, failure is null, cancellationToken);
        }
        else if (failure is null)
        {
            await RemoveRowAsync(message.AppId, cancellationToken);
        }
        else
        {
            await SettleRefusedRemovalAsync(message.AppId, cancellationToken);
        }

        return failure;
    }

    /// <summary>Consumes an operation's stream, reporting progress, and names the failure if there is one.</summary>
    /// <param name="events">The agent client's events.</param>
    /// <param name="taskId">The task to report progress against.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>
    /// Null when the stream ended in <see cref="AppOperationEventKind.Completed"/>; otherwise the failure,
    /// passed through exactly as the agent client typed it, code and kind, so a refusing package source
    /// answers 502 and an unreachable agent 503 (issue #251).
    /// </returns>
    private async Task<Error?> WatchAsync(
        IAsyncEnumerable<AppOperationEvent> events,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await foreach (var change in events.WithCancellation(cancellationToken))
        {
            switch (change.Kind)
            {
                case AppOperationEventKind.Progress:
                    await _tasks.ReportAsync(taskId, (int)change.Percent, change.Stage, cancellationToken);
                    break;

                case AppOperationEventKind.Completed:
                    await _tasks.ReportAsync(taskId, 100, change.Stage, cancellationToken);
                    return null;

                case AppOperationEventKind.Failed:
                    return change.Error ?? OutcomeUnobserved;

                // The stream ended without the agent stating whether the packages landed. These carry
                // no failure of their own, because none was reported.
                case AppOperationEventKind.Dropped:
                case AppOperationEventKind.Idle:
                case AppOperationEventKind.Truncated:
                case AppOperationEventKind.Cancelled:
                default:
                    return OutcomeUnobserved;
            }
        }

        // A stream that ended with no terminal event proves nothing about the packages.
        return OutcomeUnobserved;
    }

    /// <summary>Settles the row of an install: installed at the host's version, or failed.</summary>
    /// <param name="message">The accepted operation.</param>
    /// <param name="succeeded">Whether the stream ended in a completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the row is saved.</returns>
    private async Task SettleInstallAsync(
        AppOperationRequested message,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var row = await FindRowAsync(message.AppId, cancellationToken);
        if (row is null)
        {
            return;
        }

        var now = _clock.UtcNow;
        if (!succeeded)
        {
            row.MarkFailed(now);
        }
        else
        {
            var hostVersion = await HostVersionOfAsync(message.AppId, cancellationToken);

            // The install completed, so the row says installed even when the host could not be asked for the
            // version: an unknown version is the empty string, and the next start-up pass fills it in.
            row.MarkInstalled(hostVersion.IsSuccess ? hostVersion.Value ?? string.Empty : string.Empty, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes the row of an app the agent removed.</summary>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the row is gone.</returns>
    private async Task RemoveRowAsync(string appId, CancellationToken cancellationToken)
    {
        var row = await FindRowAsync(appId, cancellationToken);
        if (row is null)
        {
            return;
        }

        _dbContext.InstalledApps.Remove(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Settles the row of a removal that did not finish by asking the host what it has.</summary>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the row says what the host has.</returns>
    private async Task SettleRefusedRemovalAsync(string appId, CancellationToken cancellationToken)
    {
        var row = await FindRowAsync(appId, cancellationToken);
        if (row is null)
        {
            return;
        }

        var hostVersion = await HostVersionOfAsync(appId, cancellationToken);

        if (!hostVersion.IsSuccess)
        {
            // The host cannot be asked, so nobody knows what it has.
            row.MarkFailed(_clock.UtcNow);
        }
        else if (hostVersion.Value is null)
        {
            // The host does not have the app: the removal the administrator asked for is, in effect,
            // done, and a ledger row for an app that is not there would only be wrong.
            _dbContext.InstalledApps.Remove(row);
        }
        else
        {
            row.MarkInstalled(hostVersion.Value, _clock.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Marks the row failed after an exception, so it does not stay "running" for a process that moved on.</summary>
    /// <param name="message">The accepted operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the row is saved.</returns>
    private async Task SettleFailureAsync(AppOperationRequested message, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var row = await FindRowAsync(message.AppId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.MarkFailed(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Reads the ledger row of an app, logging when there is none.</summary>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tracked row, or <see langword="null"/> when none exists.</returns>
    private async Task<InstalledApp?> FindRowAsync(string appId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.InstalledApps.FirstOrDefaultAsync(app => app.AppId == appId, cancellationToken);
        if (row is null)
        {
            LogRowMissing(_logger, appId, null);
        }

        return row;
    }

    /// <summary>Asks the host which version of an app it has.</summary>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The version the host reports (possibly empty) or <see langword="null"/> when the host does not have the
    /// app, as a success; a failure when the host could not be asked, which is not the same answer as "absent".
    /// </returns>
    private async Task<Result<string?>> HostVersionOfAsync(string appId, CancellationToken cancellationToken)
    {
        var apps = await _agent.ListAppsAsync(cancellationToken);
        if (!apps.IsSuccess)
        {
            return Result<string?>.Fail(apps.Error!);
        }

        var found = apps.Value.FirstOrDefault(app =>
        {
            return string.Equals(app.AppId, appId, StringComparison.Ordinal);
        });

        return Result<string?>.Ok(found?.Version);
    }
}
