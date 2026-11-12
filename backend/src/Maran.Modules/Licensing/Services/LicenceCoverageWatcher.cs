using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Watches the licence window from outside any request and writes the changes an administrator has to
/// be able to read back to the audit journal: a module's cover ran out (lapsed) and a lapsed module is
/// covered again. It also raises the notification that the licence is close to its expiry, once for every
/// threshold the licence crosses (<see cref="LicenceExpiryWarningSource"/>). Registered as a hosted service by the
/// Host (a module may not register its own); the pass itself, <see cref="ObserveAsync"/>, is public so a test drives it without waiting for the timer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a watcher and not a write from the gate.</b> The window is asked on a request, and a request
/// has a caller. The journal's writer stamps a customer caller's account on the entry, so a lapse noticed
/// while a customer's request was in flight would be filed under that customer and shown to them: the
/// licence is the whole server's, not theirs. A pass of the panel's own has no caller, and the entry is
/// written as the panel (<see cref="LicensingAuditJournal.RecordCoverageTransitionAsync"/>).
/// </para>
/// <para>
/// <b>What it costs, said plainly.</b> A lapse is journalled up to <see cref="Interval"/> plus the
/// window's own 30-second memory after the cover ended, and the entry's time is the time the panel
/// noticed. A lapse that began and ended between two passes is not seen. The refusal of the paid module
/// does not wait for the watcher: the gate asks the window itself on every request.
/// </para>
/// <para>
/// <b>It reads through <see cref="LicenceCoverageCache"/>, the same memory the gate reads</b>, so what
/// the journal says and what the gate did cannot differ. What it remembers (the last verdict per module)
/// lives in this process: after a restart a module found lapsed is journalled again, and
/// <see cref="CoverageTransitionPolicy"/> says what that entry is. The one exception is the expiry announcement: which
/// threshold the operator was told about is stored (<see cref="ILicenceExpiryAnnouncementStore"/>), so a restart or a
/// crash loop does not tell them again.
/// </para>
/// <para>
/// <b>Never takes the core down.</b> Every pass runs inside one catch that logs and goes on; a failed
/// journal write leaves the module's remembered verdict unchanged, so the next pass tries again. Only the
/// modules the licence names (and those already remembered) are asked about, so a panel with no licence
/// does nothing here.
/// </para>
/// </remarks>
public sealed class LicenceCoverageWatcher : BackgroundService
{
    /// <summary>How long between passes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>How long after the start the first pass runs, so it does not compete with the start-up work.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    /// <summary>A pass failed.</summary>
    private static readonly Action<ILogger, string, Exception?> LogPassFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(LogPassFailed)),
            "Watching the licence cover failed ({ExceptionType}); it will be tried again at the next interval.");

    /// <summary>A lapse or recovery could not be written to the journal.</summary>
    private static readonly Action<ILogger, CoverageTransition, string, Exception?> LogJournalFailed =
        LoggerMessage.Define<CoverageTransition, string>(
            LogLevel.Error,
            new EventId(2, nameof(LogJournalFailed)),
            "Journalling the {Transition} of module {ModuleId} failed; it will be tried again at the next interval.");

    /// <summary>A licence expiry alert could not be raised.</summary>
    private static readonly Action<ILogger, string, Exception?> LogExpiryAlertFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(LogExpiryAlertFailed)),
            "Raising the licence expiry alert failed ({ExceptionType}); it is tried again at the next interval unless its journal entry was already written.");

    /// <summary>The note of what was announced could not be stored or removed.</summary>
    private static readonly Action<ILogger, string, Exception?> LogAnnouncementNotStored =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, nameof(LogAnnouncementNotStored)),
            "Storing which licence expiry threshold was announced failed ({ExceptionType}); it is remembered until the panel restarts.");

    /// <summary>Reads the licence and lease the way the gate does.</summary>
    private readonly LicenceCoverageCache _cache;

    /// <summary>Says whether the licence is close to its expiry.</summary>
    private readonly LicenceExpiryWarningSource _expiryWarnings;

    /// <summary>Keeps what the operator was last told about the expiry across restarts.</summary>
    private readonly ILicenceExpiryAnnouncementStore _announcements;

    /// <summary>Opens the scope the journal (a scoped service) is resolved from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Operator-facing log.</summary>
    private readonly ILogger<LicenceCoverageWatcher> _logger;

    /// <summary>Serialises passes, so a test's pass and the timer's cannot interleave.</summary>
    private readonly SemaphoreSlim _passGate = new(1, 1);

    /// <summary>The verdict last journalled or found unchanged for each module, by module id.</summary>
    private readonly Dictionary<string, EntitlementVerdict> _last = new(StringComparer.Ordinal);

    /// <summary>
    /// The expiry and threshold the operator was last told about, or null when none stands. It is the stored note
    /// (<see cref="ILicenceExpiryAnnouncementStore"/>) read once, then kept current, so a restart does not announce
    /// the same threshold again.
    /// </summary>
    private LicenceExpiryAnnouncement? _announced;

    /// <summary>Whether <see cref="_announced"/> has been read from the store yet.</summary>
    private bool _announcedLoaded;

    /// <summary>Creates the watcher.</summary>
    /// <param name="cache">Reads the licence and lease the way the gate does.</param>
    /// <param name="expiryWarnings">Says whether the licence is close to its expiry.</param>
    /// <param name="announcements">Keeps what the operator was last told about the expiry across restarts.</param>
    /// <param name="scopeFactory">Opens the scope the journal is resolved from.</param>
    /// <param name="logger">Operator-facing log.</param>
    public LicenceCoverageWatcher(
        LicenceCoverageCache cache,
        LicenceExpiryWarningSource expiryWarnings,
        ILicenceExpiryAnnouncementStore announcements,
        IServiceScopeFactory scopeFactory,
        ILogger<LicenceCoverageWatcher> logger)
    {
        _cache = cache;
        _expiryWarnings = expiryWarnings;
        _announcements = announcements;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Reads the cover once and journals each module whose cover lapsed or recovered since the last pass.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves when the pass is done.</returns>
    /// <exception cref="Exception">The licence could not be read; <see cref="ExecuteAsync"/> logs it and goes on.</exception>
    public async Task ObserveAsync(CancellationToken cancellationToken)
    {
        await _passGate.WaitAsync(cancellationToken);
        try
        {
            var coverage = await _cache.GetAsync(cancellationToken);

            foreach (var moduleId in ModulesToAskAbout(coverage))
            {
                await ObserveModuleAsync(coverage, moduleId, cancellationToken);
            }

            await ObserveExpiryAsync(coverage, cancellationToken);
        }
        finally
        {
            _passGate.Release();
        }
    }

    /// <summary>Runs a pass on the interval until the host shuts down; no pass failure ends the loop.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>Resolves when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            do
            {
                await ObserveGuardedAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure.
        }
    }

    /// <summary>Runs one pass and turns any failure into a log line.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves when the pass is done or has failed.</returns>
    private async Task ObserveGuardedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ObserveAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The core must outlive any licensing failure (spec 229): log the type only, never the text,
            // which could quote a path or a licence fragment.
            LogPassFailed(_logger, exception.GetType().Name, null);
        }
    }

    /// <summary>The modules this pass has something to say about: those the licence names and those remembered.</summary>
    /// <param name="coverage">The licence and lease as the gate sees them.</param>
    /// <returns>The module ids, each once.</returns>
    private List<string> ModulesToAskAbout(LicenceCoverage coverage)
    {
        IReadOnlyList<string> named = coverage.Status switch
        {
            LicenceStatus.Valid valid => valid.Licence.Modules,
            LicenceStatus.Refused { LapsedLicence: { } lapsed } => lapsed.Modules,
            _ => [],
        };

        return named.Concat(_last.Keys).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Judges one module and journals a lapse or a recovery.</summary>
    /// <param name="coverage">The licence and lease as the gate sees them.</param>
    /// <param name="moduleId">The module.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves when the module has been judged.</returns>
    private async Task ObserveModuleAsync(LicenceCoverage coverage, string moduleId, CancellationToken cancellationToken)
    {
        var current = ModuleCoveragePolicy.VerdictOf(coverage, moduleId).Verdict;
        EntitlementVerdict? previous = _last.TryGetValue(moduleId, out var remembered) ? remembered : null;
        var transition = CoverageTransitionPolicy.Classify(previous, current);

        if (transition != CoverageTransition.None && !await TryJournalAsync(moduleId, previous, current, transition, cancellationToken))
        {
            return;
        }

        _last[moduleId] = current;
    }

    /// <summary>Raises the expiry alert when the licence has crossed a threshold the operator was not told about yet.</summary>
    /// <remarks>
    /// <para>
    /// <b>What resets the memory.</b> Only a licence that was READ and is not within any threshold: a new licence
    /// further out, or a configuration with no thresholds. A pass that finds no licence, or one that does not verify
    /// (a file being replaced, unreadable for one pass), says nothing about the threshold already announced and leaves
    /// it, so the next good pass does not announce it again. A licence with another expiry is another fact and is
    /// announced on its own.
    /// </para>
    /// <para>
    /// <b>Told, then remembered, at the journal row.</b> The note is stored as soon as the alert's journal row exists
    /// and before the outputs are published. A failure before the row (the recipients could not be read) stores nothing
    /// and the next pass tries the whole alert again; a failure after the row (an output) is logged and not repeated,
    /// because the row is the fact and a retry would only write it a second time. A crash between the row and the note
    /// repeats one notice and never loses one. A note that cannot be stored is logged and kept in memory.
    /// </para>
    /// </remarks>
    /// <param name="coverage">The licence and lease as the gate sees them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves when the licence has been judged.</returns>
    private async Task ObserveExpiryAsync(LicenceCoverage coverage, CancellationToken cancellationToken)
    {
        await LoadAnnouncedAsync(cancellationToken);

        var warning = _expiryWarnings.For(coverage.Status);

        if (warning is null)
        {
            if (coverage.Status is LicenceStatus.Valid)
            {
                // A verified licence outside every threshold: the next crossing is announced.
                await TryStoreAsync(null, cancellationToken);
            }

            return;
        }

        var announcement = new LicenceExpiryAnnouncement(warning.Expiry, warning.ThresholdDays);
        if (_announced == announcement)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LicenceExpiryAlerter>().RaiseAsync(
                warning,
                cancellationToken,
                token => { return TryStoreAsync(announcement, token); });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogExpiryAlertFailed(_logger, exception.GetType().Name, null);
        }
    }

    /// <summary>Reads the stored note once, the first time an expiry is judged.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves when the note is in memory.</returns>
    private async Task LoadAnnouncedAsync(CancellationToken cancellationToken)
    {
        if (_announcedLoaded)
        {
            return;
        }

        _announced = await _announcements.ReadAsync(cancellationToken);
        _announcedLoaded = true;
    }

    /// <summary>Makes an announcement the current one, in memory and in the store; a store failure is logged, not thrown.</summary>
    /// <param name="announcement">The announcement to remember, or null to forget.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves when the memory is updated and the store has been tried.</returns>
    private async Task TryStoreAsync(LicenceExpiryAnnouncement? announcement, CancellationToken cancellationToken)
    {
        if (_announced == announcement)
        {
            return;
        }

        _announced = announcement;

        try
        {
            await (announcement is null
                ? _announcements.ForgetAsync(cancellationToken)
                : _announcements.RememberAsync(announcement, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAnnouncementNotStored(_logger, exception.GetType().Name, null);
        }
    }

    /// <summary>Writes one entry as the panel; a failure is logged and reported, not thrown.</summary>
    /// <param name="moduleId">The module.</param>
    /// <param name="previous">The previous verdict, or null.</param>
    /// <param name="current">The verdict now.</param>
    /// <param name="transition">What the change is.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entry is stored.</returns>
    private async Task<bool> TryJournalAsync(
        string moduleId,
        EntitlementVerdict? previous,
        EntitlementVerdict current,
        CoverageTransition transition,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var journal = scope.ServiceProvider.GetRequiredService<LicensingAuditJournal>();
            await journal.RecordCoverageTransitionAsync(moduleId, previous, current, transition, cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogJournalFailed(_logger, transition, moduleId, exception);

            return false;
        }
    }
}
