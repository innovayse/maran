using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.Policies;
using Maran.Modules.Updates.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// Closes the task of a requested update whose report never arrived, so a task is never left running for
/// ever (issue #382).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The update restarts the panel, so the startup pass that closes tasks a dead
/// process left running deliberately skips this kind (<see cref="TaskKinds.PanelUpdate"/>). Normally the
/// root update's report closes the task. If the root unit never started, the report command failed, or the
/// machine rebooted, nothing would — and this pass is that something.
/// </para>
/// <para>
/// <b>What it decides is the policy's.</b> <see cref="PanelUpdateLedgerPolicy.Decide"/> holds the
/// thresholds; this class reads the file system, asks, and acts. A marker root never picked up is
/// withdrawn, so the next request is not refused for ever. It never throws out of its loop: a pass that
/// fails is logged at warning and tried again at the next tick.
/// </para>
/// </remarks>
public sealed class PanelUpdateTaskReaper : BackgroundService
{
    /// <summary>How long between passes, and before the first.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>Pre-compiled log delegate for a pass that failed.</summary>
    private static readonly Action<ILogger, Exception?> LogPassFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(PanelUpdateTaskReaper)),
            "Checking for an abandoned panel update request failed; it is tried again at the next pass");

    /// <summary>Reads the marker and root's running record.</summary>
    private readonly PanelUpdateStateReader _reader;

    /// <summary>Names the task an open request opened.</summary>
    private readonly PanelUpdateTaskLedger _ledger;

    /// <summary>Withdraws a marker root never picked up.</summary>
    private readonly PanelUpdateRequestMarker _marker;

    /// <summary>Opens a scope per pass, because the task recorder and the journal are scoped.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where a failed pass becomes visible to an operator.</summary>
    private readonly ILogger<PanelUpdateTaskReaper> _logger;

    /// <summary>Creates the reaper.</summary>
    /// <param name="reader">Reads the marker and root's running record.</param>
    /// <param name="ledger">Names the task an open request opened.</param>
    /// <param name="marker">Withdraws a marker root never picked up.</param>
    /// <param name="scopeFactory">Opens a scope per pass.</param>
    /// <param name="logger">Where a failed pass is reported.</param>
    public PanelUpdateTaskReaper(
        PanelUpdateStateReader reader,
        PanelUpdateTaskLedger ledger,
        PanelUpdateRequestMarker marker,
        IServiceScopeFactory scopeFactory,
        ILogger<PanelUpdateTaskReaper> logger)
    {
        _reader = reader;
        _ledger = ledger;
        _marker = marker;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Runs one pass.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The verdict the pass acted on; <see cref="PanelUpdateLedgerVerdict.Leave"/> is the ordinary answer.</returns>
    /// <remarks>
    /// Public because it is the pass, and the pass is what has behaviour worth asserting: a test drives it
    /// directly rather than waiting for a timer, which is the sleep rules/testing.md forbids.
    /// </remarks>
    public async Task<PanelUpdateLedgerVerdict> ReapAsync(CancellationToken cancellationToken)
    {
        var verdict = PanelUpdateLedgerPolicy.Decide(_reader.ReadSignals(), _ledger.Age());
        if (verdict == PanelUpdateLedgerVerdict.Leave)
        {
            return verdict;
        }

        var taskId = _ledger.TryRead();
        var code = nameof(ErrorMessages.PanelUpdateOutcomeUnknown);
        var action = UpdatesAuditActions.PanelUpdateRunOutcomeUnknown;
        if (verdict == PanelUpdateLedgerVerdict.NeverStarted)
        {
            code = nameof(ErrorMessages.PanelUpdateNotStarted);
            action = UpdatesAuditActions.PanelUpdateRunNotStarted;
            if (!_marker.TryRemove())
            {
                // A marker that cannot be withdrawn keeps every later request refused; the next pass tries again.
                return PanelUpdateLedgerVerdict.Leave;
            }
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        if (taskId is { } task)
        {
            await scope.ServiceProvider.GetRequiredService<ITaskRecorder>().FailAsync(task, code, cancellationToken);
        }

        _ledger.Remove();
        await scope.ServiceProvider.GetRequiredService<UpdatesAuditJournal>()
            .RecordSystemAsync(action, "request=update", succeeded: false, cancellationToken);

        return verdict;
    }

    /// <summary>Runs a pass every <see cref="Interval"/> until the host stops.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>Resolves when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PassAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and deliberately not logged as one.
        }
    }

    /// <summary>Runs one pass, turning anything it throws into a log line.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>Resolves once the pass has finished or failed.</returns>
    private async Task PassAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReapAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A loop that dies on its first failure stops closing tasks for the life of the process; the
            // failure is logged, with no path in it, and the next tick tries again.
            LogPassFailed(_logger, exception);
        }
    }
}
