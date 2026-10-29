using Maran.Agent.Client.Services.SystemService;
using Maran.Host.HealthChecks;

namespace Maran.Host.BackgroundServices;

/// <summary>
/// Asks the agent for its contract revision once, right after the panel starts, and tells the
/// operator in one log line whether this panel can drive it. Registered by
/// <c>Maran.Host.Extensions.HealthCheckExtensions</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reports; it never stops the panel.</b> An incompatible agent is an upgrade that replaced one
/// side and not the other, and the remedy is to finish the upgrade — not to take the panel down.
/// Exiting would make it worse. An exception escaping a <see cref="BackgroundService"/> stops the
/// host, and <c>maran-api.service</c> then either restarts it (<c>Restart=on-failure</c>, on a failure
/// status) until systemd gives up after five starts in a minute (<c>StartLimitBurst=5</c>,
/// <c>StartLimitIntervalSec=60</c>), or, on a clean status, does not restart it at all — either way a
/// panel that does not run, behind an nginx answering 502, over a mismatch an operator could have
/// fixed from the panel's own health report. So nothing this check meets escapes it except the
/// host's own shutdown: the panel keeps serving its own pages and <c>/health/live</c>, and the
/// refusal lasts where it can be observed — <see cref="AgentHealthProbe"/> reports
/// <see cref="AgentHealthProbe.Incompatible"/>, and <c>/health/ready</c> answers 503, on every request
/// until a compatible agent answers.
/// </para>
/// <para>
/// <b>Why once, and why this line is not the lasting answer.</b> The verdict is read through the same
/// <see cref="AgentHealthProbe"/> the health routes use, and nothing caches it: an agent restarted or
/// upgraded after this line was written is seen by the next health request, without restarting the
/// API. This line exists so an operator who reads the journal after an upgrade sees the mismatch
/// stated at the moment it began, with the window it was measured against; the revision the agent
/// reported is in <see cref="AgentSystemClient"/>'s warning written by the same handshake.
/// </para>
/// <para>
/// <b>Why a <see cref="BackgroundService"/>, and why it yields first.</b> The host must not wait for
/// the agent to start: a fresh boot may bring the panel up before the agent, and every test host runs
/// with no agent at all. <see cref="BackgroundService.StartAsync"/> returns as soon as
/// <see cref="ExecuteAsync"/> first yields, and the first statement of <see cref="ExecuteAsync"/> is a
/// yield, so no agent I/O — not even a connect that fails synchronously — runs on the host's startup
/// path.
/// </para>
/// </remarks>
public sealed class AgentCompatibilityStartupCheck : BackgroundService
{
    /// <summary>Pre-compiled log delegate for an agent this panel can drive.</summary>
    private static readonly Action<ILogger, uint, uint, Exception?> LogCompatible =
        LoggerMessage.Define<uint, uint>(
            LogLevel.Information,
            new EventId(1, nameof(AgentCompatibilityStartupCheck)),
            "Agent check at startup: the agent speaks a contract revision inside "
                + "{MinimumRevision}..{MaximumRevision}, the revisions this panel drives");

    /// <summary>
    /// Pre-compiled log delegate for an agent that did not answer. A warning, not an error: the panel
    /// may simply have started first, and it serves its own pages without an agent either way.
    /// </summary>
    private static readonly Action<ILogger, Exception?> LogUnavailable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(AgentCompatibilityStartupCheck)),
            "Agent check at startup: the agent did not answer the handshake. The panel starts regardless; "
                + "/health reports the agent unavailable, and agent actions fail, until it answers");

    /// <summary>Pre-compiled log delegate for the mismatch this check exists to state.</summary>
    private static readonly Action<ILogger, uint, uint, Exception?> LogIncompatible =
        LoggerMessage.Define<uint, uint>(
            LogLevel.Error,
            new EventId(3, nameof(AgentCompatibilityStartupCheck)),
            "Agent check at startup: the agent speaks a contract revision outside "
                + "{MinimumRevision}..{MaximumRevision}, the revisions this panel drives, so the panel and the "
                + "agent come from different releases. The panel keeps running; /health reports the agent "
                + "incompatible and /health/ready answers 503 until an agent from this panel's release "
                + "answers. Finish the upgrade so both come from one release");

    /// <summary>Pre-compiled log delegate for the guard itself catching something.</summary>
    private static readonly Action<ILogger, Exception?> LogGuardCaught =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4, nameof(AgentCompatibilityStartupCheck)),
            "Agent check at startup did not complete; the panel is unaffected and starts normally regardless");

    /// <summary>The probe the health routes read, so this check and readiness can never disagree.</summary>
    private readonly AgentHealthProbe _probe;

    /// <summary>Where the verdict is reported.</summary>
    private readonly ILogger<AgentCompatibilityStartupCheck> _logger;

    /// <summary>Creates the check.</summary>
    /// <param name="probe">The agent probe the health routes use.</param>
    /// <param name="logger">Where the verdict is reported.</param>
    public AgentCompatibilityStartupCheck(AgentHealthProbe probe, ILogger<AgentCompatibilityStartupCheck> logger)
    {
        _probe = probe;
        _logger = logger;
    }

    /// <summary>
    /// Probes the agent once and logs the verdict — never throwing. Public and separate from
    /// <see cref="ExecuteAsync"/> so a test can assert directly that nothing the probe does escapes it,
    /// without starting and stopping a whole <see cref="BackgroundService"/>.
    /// </summary>
    /// <param name="cancellationToken">The host's stopping token, forwarded to the probe.</param>
    /// <returns>A task that faults only with the cancellation of <paramref name="cancellationToken"/>.</returns>
    /// <remarks>
    /// The catch of every other exception is this type's whole reason for being written as it is (see
    /// the remarks on the type): the probe lets a defect of the panel itself escape on purpose, so a
    /// health REQUEST fails loudly, and that same defect must not reach the host from here, where it
    /// would stop the panel. It is logged at error level, not swallowed.
    /// </remarks>
    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var verdict = await _probe.ProbeAsync(cancellationToken);

            switch (verdict)
            {
                case AgentHealthProbe.Connected:
                    LogCompatible(_logger, AgentProtoVersionWindow.Minimum, AgentProtoVersionWindow.Maximum, null);
                    break;
                case AgentHealthProbe.Incompatible:
                    LogIncompatible(_logger, AgentProtoVersionWindow.Minimum, AgentProtoVersionWindow.Maximum, null);
                    break;
                default:
                    LogUnavailable(_logger, null);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogGuardCaught(_logger, exception);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only the shutdown case is handled here; every other failure is <see cref="CheckOnceAsync"/>'s own
    /// guard to enforce.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hand control back to the host before any agent I/O, so a connect that fails synchronously
        // still runs off the startup path (see the remarks on the type).
        await Task.Yield();

        try
        {
            await CheckOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host stopped before the agent answered. Not a finding about the agent.
        }
    }
}
