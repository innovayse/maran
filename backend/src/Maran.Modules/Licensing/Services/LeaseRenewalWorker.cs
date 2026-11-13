using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Runs the licence lease renewal on its schedule and turns what each pass did into the journal entry and the
/// operator alert it owes. Registered as a hosted service by the Host (a module may not register its own); the
/// pass itself, <see cref="RunPassAsync"/>, is public so a test drives it without waiting for the timer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The schedule is <see cref="LeaseRenewalPolicy"/>'s.</b> A first pass a few minutes after start, then a wait
/// that depends on the last outcome: about a day after a stored lease, a few hours after a failure, an hour
/// when there was nothing to do. Every wait carries a random share of a span so panels installed from one image
/// do not ask the service at the same minute. The worker asks the policy and sleeps; it decides nothing.
/// </para>
/// <para>
/// <b>What it remembers</b> (the run of failed passes, and whether the alert for that run was raised) lives in
/// this process. After a restart the run starts again at zero, so an outage that spans a restart alerts once the
/// new process has failed three times itself, and a recovery after a restart does not resolve an alert the new
/// process never raised. Stated, not hidden.
/// </para>
/// <para>
/// <b>Never takes the core down.</b> Every pass runs inside one catch that logs the exception type and goes on;
/// a failed journal write or alert is logged and the next pass tries to say it again, because the state that
/// says it was said is only updated once it was.
/// </para>
/// <para>
/// <b>Journal.</b> A stored lease and every failed pass are journalled as the panel; a pass with nothing to do
/// (not due, no licence, offline, no address) is not, or the trail would carry an entry an hour.
/// </para>
/// <para>
/// <b>A pass can also be asked for (<see cref="RequestNowAsync"/>), and it is the same pass.</b> Installing a licence
/// asks for one at once, so the paid modules are not lapsed for the hour the schedule would otherwise wait. The
/// request goes through <see cref="RunPassAsync"/>, so it is serialised with the timer's passes, journalled and
/// alerted like them and counted in the same run of failures; and the loop does not keep the wait it had chosen
/// before the request. It starts the wait again from the requested pass's outcome (a day after a stored lease, hours
/// after a failure), so a request that failed is followed by the failure wait and not by the idle hour that preceded
/// it, and one that stored a lease is not followed by a second request an hour later.
/// </para>
/// <para>
/// <b>A request is bounded, a scheduled pass is not.</b> The request is a web request: it carries one deadline
/// (<see cref="LeaseServiceOptions.InstallRequestDeadlineSeconds"/>) that covers both the wait for the pass gate and
/// the request to the licence service, so it answers before a reverse proxy gives up on it. When a scheduled pass
/// holds the gate for the whole deadline the request reports <see cref="LeaseRenewalOutcome.RenewalInProgress"/> and
/// runs nothing; when the service does not answer in time it reports <see cref="LeaseRenewalOutcome.Unreachable"/>,
/// journalled and counted like any failed pass. Either way the licence is installed and the schedule carries on.
/// </para>
/// </remarks>
public sealed class LeaseRenewalWorker : BackgroundService, ILeaseRenewalRequester
{
    /// <summary>A pass failed in a way the renewer does not turn into an outcome.</summary>
    private static readonly Action<ILogger, string, Exception?> LogPassFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(LogPassFailed)),
            "Renewing the licence lease failed ({ExceptionType}); it will be tried again later.");

    /// <summary>The journal entry or the alert of a pass could not be written.</summary>
    private static readonly Action<ILogger, LeaseRenewalOutcome, string, Exception?> LogReportFailed =
        LoggerMessage.Define<LeaseRenewalOutcome, string>(
            LogLevel.Error,
            new EventId(2, nameof(LogReportFailed)),
            "Reporting the {Outcome} lease renewal failed ({ExceptionType}); an alert it owed is repeated at the next pass that calls for it.");

    /// <summary>Runs one renewal pass.</summary>
    private readonly LeaseRenewer _renewer;

    /// <summary>Opens the scope the journal and the alerter (scoped services) are resolved from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Operator-facing log.</summary>
    private readonly ILogger<LeaseRenewalWorker> _logger;

    /// <summary>Serialises passes, so a test's pass and the timer's cannot interleave.</summary>
    private readonly SemaphoreSlim _passGate = new(1, 1);

    /// <summary>The length of the current run of failed passes; 0 after a stored lease.</summary>
    private int _consecutiveFailures;

    /// <summary>Whether the alert for the current run of failures has been raised.</summary>
    private bool _alertRaised;

    /// <summary>The deadline an install's request allows itself, gate wait included.</summary>
    private readonly TimeSpan _installDeadline;

    /// <summary>The wait between passes, which a pass somebody asked for ends early.</summary>
    private readonly LeaseWaitSignal _wait = new();

    /// <summary>Creates the worker.</summary>
    /// <param name="renewer">Runs one renewal pass.</param>
    /// <param name="scopeFactory">Opens the scope the journal and the alerter are resolved from.</param>
    /// <param name="logger">Operator-facing log.</param>
    /// <param name="options">The lease options; the install deadline is read from them.</param>
    public LeaseRenewalWorker(
        LeaseRenewer renewer,
        IServiceScopeFactory scopeFactory,
        ILogger<LeaseRenewalWorker> logger,
        IOptions<LeaseServiceOptions> options)
    {
        _renewer = renewer;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _installDeadline = TimeSpan.FromSeconds(options.Value.InstallRequestDeadlineSeconds);
    }

    /// <summary>
    /// Runs one renewal pass and reports it: the journal entry for a stored lease or a failure, and the alert
    /// when the run of failures reaches the threshold or ends.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the pass did.</returns>
    public async Task<LeaseRenewalResult> RunPassAsync(CancellationToken cancellationToken)
    {
        await _passGate.WaitAsync(cancellationToken);
        try
        {
            var result = await _renewer.RenewAsync(cancellationToken);

            await ReportAsync(result, cancellationToken);

            return result;
        }
        finally
        {
            _passGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<LeaseRenewalResult> RequestNowAsync(string licenceId, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_installDeadline);

        // The guarded pass, so an administrator's install cannot fail because a renewal pass did (spec 229).
        var result = await RunBoundedAsync(licenceId, deadline.Token, cancellationToken);

        // A request that ran nothing has no outcome for the schedule to wait on.
        if (result.Outcome != LeaseRenewalOutcome.RenewalInProgress)
        {
            _wait.Notify(result.Outcome);
        }

        return result;
    }

    /// <summary>Waits, runs a pass, waits as the last outcome says, until the host shuts down; no failure ends the loop.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>Resolves when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var delay = LeaseRenewalPolicy.DelayBeforeFirstPass(Random.Shared.NextDouble());
            while (true)
            {
                var requested = await _wait.WaitAsync(delay, stoppingToken);
                if (requested is not null)
                {
                    // Somebody asked for a pass and it ran: wait as that pass's outcome says instead of running another.
                    delay = LeaseRenewalPolicy.DelayAfter(requested.Value, Random.Shared.NextDouble());
                    continue;
                }

                var result = await RunGuardedAsync(stoppingToken);
                delay = LeaseRenewalPolicy.DelayAfter(result.Outcome, Random.Shared.NextDouble());
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure.
        }
    }

    /// <summary>
    /// Runs one pass for one licence within the install deadline: the wait for the gate and the request share it.
    /// </summary>
    /// <param name="licenceId">The licence the caller means.</param>
    /// <param name="deadlineToken">Cancelled by the deadline or by the caller.</param>
    /// <param name="callerToken">The caller's own token; its cancellation propagates, the deadline's does not.</param>
    /// <returns>The pass's result, <see cref="LeaseRenewalOutcome.RenewalInProgress"/>, or <see cref="LeaseRenewalOutcome.Unreachable"/>.</returns>
    private async Task<LeaseRenewalResult> RunBoundedAsync(
        string licenceId,
        CancellationToken deadlineToken,
        CancellationToken callerToken)
    {
        try
        {
            await _passGate.WaitAsync(deadlineToken);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            return LeaseRenewalResult.Of(LeaseRenewalOutcome.RenewalInProgress);
        }

        try
        {
            LeaseRenewalResult result;
            try
            {
                result = await _renewer.RenewAsync(licenceId, deadlineToken);
            }
            catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
            {
                // The deadline ended the request: the service did not answer in time.
                result = LeaseRenewalResult.Of(LeaseRenewalOutcome.Unreachable);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The core must outlive any licensing failure (spec 229): type only, as in RunGuardedAsync. Deliberately
                // not a type list: this guard is what keeps an administrator's licence install from failing because
                // a renewal pass did, whatever the pass raised.
                LogPassFailed(_logger, exception.GetType().Name, null);
                result = LeaseRenewalResult.Of(LeaseRenewalOutcome.Unreachable);
            }

            await ReportAsync(result, callerToken);

            return result;
        }
        finally
        {
            _passGate.Release();
        }
    }

    /// <summary>Runs one pass and turns any failure into a log line and the failure wait.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pass's result, or an <see cref="LeaseRenewalOutcome.Unreachable"/> one when the pass itself threw.</returns>
    private async Task<LeaseRenewalResult> RunGuardedAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RunPassAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The core must outlive any licensing failure (spec 229): log the type only, never the text,
            // which could quote a path or a licence fragment. The failure wait applies.
            LogPassFailed(_logger, exception.GetType().Name, null);

            return LeaseRenewalResult.Of(LeaseRenewalOutcome.Unreachable);
        }
    }

    /// <summary>Counts the pass, journals what is journalled and raises or resolves the alert.</summary>
    /// <param name="result">What the pass did.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves when the pass has been reported or the report has failed.</returns>
    private async Task ReportAsync(LeaseRenewalResult result, CancellationToken cancellationToken)
    {
        var failed = LeaseRenewalPolicy.IsFailure(result.Outcome);

        // A pass with nothing to do (not due, no licence, offline, no address) neither extends nor ends a run
        // of failures and writes nothing.
        if (!failed && result.Outcome != LeaseRenewalOutcome.Renewed)
        {
            return;
        }

        var failures = failed ? _consecutiveFailures + 1 : 0;
        var action = LeaseRenewalPolicy.AlertActionFor(result.Outcome, failures, _alertRaised);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var journal = scope.ServiceProvider.GetRequiredService<LicensingAuditJournal>();
            await journal.RecordLeaseRenewalAsync(result, failures, cancellationToken);

            if (action == LeaseRenewalAlertAction.Raise)
            {
                await scope.ServiceProvider.GetRequiredService<LeaseRenewalAlerter>()
                    .RaiseAsync(result.Outcome, failures, cancellationToken);
                _alertRaised = true;
            }
            else if (action == LeaseRenewalAlertAction.Resolve)
            {
                await scope.ServiceProvider.GetRequiredService<LeaseRenewalAlerter>().ResolveAsync(cancellationToken);
                _alertRaised = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogReportFailed(_logger, result.Outcome, exception.GetType().Name, null);
        }

        _consecutiveFailures = failures;
    }
}
