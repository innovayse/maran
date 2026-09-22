using Maran.Modules.Licensing.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Runs <see cref="LicenceVerifier"/> once, after the panel has started, and logs the operator-facing
/// sentence for whatever it finds. Registered by <c>Maran.Host.Extensions.LicensingExtensions</c>,
/// never by <see cref="LicensingModule"/> itself — a module may not register its own hosted service,
/// the same split <c>BackgroundWorkExtensions</c> and <c>SeedingExtensions</c> already use.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is where §229 ("the core never dies") is actually enforced for a caller, not merely
/// promised by <see cref="LicenceVerifier"/>'s own contract.</b> <see cref="LicenceVerifier.VerifyAsync"/>
/// already documents itself as never throwing, but a hosted service that TRUSTED that promise with no
/// guard of its own would still take the whole panel down the day that promise is broken by a future
/// edit — the exact single point of failure the threat note (<c>docs/superpowers/notes/2026-09-22-
/// licence-verification-threat-note.md</c> §2) warns against. So <see cref="ExecuteAsync"/> wraps its
/// entire body, source read through logging, in one <c>try/catch (Exception)</c> that never rethrows.
/// That is deliberately redundant with <see cref="LicenceVerifier"/>'s own internal guard: two
/// independent places would have to fail the same way at once for a licensing bug to reach the host,
/// rather than one deletable try/catch being the sole thing standing between a verifier defect and an
/// outage.
/// </para>
/// <para>
/// <b>Why a <see cref="BackgroundService"/> and not synchronous startup work.</b> Even a
/// try/catch-guarded step run directly inside <c>IHostedService.StartAsync</c> and AWAITED there
/// still delays every other <c>StartAsync</c> after it and holds up <c>/health</c> answering at all
/// until it finishes — for a step whose entire purpose is operator-facing information, not something
/// any other startup step depends on. <see cref="BackgroundService.StartAsync"/> instead starts
/// <see cref="ExecuteAsync"/> and returns without awaiting it to completion (the moment this method
/// reaches its first <c>await</c>, control returns to the host), so this check runs concurrently with
/// the rest of startup and can never be the reason startup is slow, in addition to never being the
/// reason it fails.
/// </para>
/// </remarks>
public sealed class LicenceStartupCheck : BackgroundService
{
    /// <summary>Pre-compiled log delegate for a successful observation.</summary>
    private static readonly Action<ILogger, string, Exception?> LogObserved =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(LicenceStartupCheck)),
            "Licence check at startup: {Sentence}");

    /// <summary>Pre-compiled log delegate for the guard itself catching something.</summary>
    private static readonly Action<ILogger, Exception?> LogGuardCaught =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(LicenceStartupCheck)),
            "Licence check at startup did not complete; the panel is unaffected and starts normally regardless");

    /// <summary>Opens one scope to resolve this pass's scoped dependencies from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where the outcome (or the guard catching something) is reported.</summary>
    private readonly ILogger<LicenceStartupCheck> _logger;

    /// <summary>Creates the check.</summary>
    /// <param name="scopeFactory">Opens the scope this pass resolves its scoped dependencies from.</param>
    /// <param name="logger">Where the outcome is reported.</param>
    public LicenceStartupCheck(IServiceScopeFactory scopeFactory, ILogger<LicenceStartupCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only the shutdown case is handled here; every other failure is <see cref="CheckOnceAsync"/>'s
    /// own guard to enforce, kept separate so a test can drive that guard directly without starting
    /// and stopping a whole <see cref="BackgroundService"/> (rules/testing.md forbids sleeping for a
    /// background pass to happen).
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await CheckOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown before this pass ran. Not a failure — see StartupBanReconciler's own
            // ExecuteAsync for the identical reasoning this module follows.
        }
    }

    /// <summary>
    /// Reads the installed licence, verifies it, and logs the operator-facing sentence for whatever
    /// it finds — never throwing. This IS the §229 guard this type's own remarks describe: public and
    /// separate from <see cref="ExecuteAsync"/> specifically so a test can assert directly that
    /// nothing this method's body can do — including a scoped dependency itself throwing — escapes it.
    /// </summary>
    /// <param name="cancellationToken">Forwarded to <see cref="LicenceVerifier.VerifyAsync"/>.</param>
    /// <returns>A task that never faults.</returns>
    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var rawTextSource = scope.ServiceProvider.GetRequiredService<ILicenceRawTextSource>();
            var verifier = scope.ServiceProvider.GetRequiredService<LicenceVerifier>();
            var displayNames = scope.ServiceProvider.GetRequiredService<LicenceStatusDisplayNames>();

            var rawLicenceText = rawTextSource.ReadRawLicenceText();
            var status = await verifier.VerifyAsync(rawLicenceText, cancellationToken);

            LogObserved(_logger, displayNames.SentenceFor(status), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The guard this type's own remarks describe: whatever broke — including the raw-text
            // source itself throwing, which is exactly what the Host-level tests simulate — the
            // panel does not care.
            LogGuardCaught(_logger, exception);
        }
    }
}
