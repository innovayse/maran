using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>
/// Carries out the loader's request to restart the panel so that a staged module activates: it records
/// the request in the audit journal, remembers that it was made, and asks the host to stop. The process
/// then exits with <see cref="ExitStatus"/>, which is non-zero so that the service manager starts it
/// again (<c>Restart=on-failure</c> in <c>maran-api.service</c>); a zero exit would be a clean stop and
/// nothing would bring the panel back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why activation is a restart.</b> Message handlers, validators, controllers and migrations are all
/// taken from the module list when the host is BUILT, and Wolverine cannot take new handlers once it
/// has started. A restart is the one way to activate a package that does not rest on that, and it has a
/// property hot loading does not: every installed package is verified again at every start, so what
/// runs is what passed the checks THIS time, not what passed them when the process began.
/// </para>
/// <para>
/// <b>Idempotent.</b> A second request while one is pending is recorded and changes nothing else.
/// It adds no listener, no daemon and no outbound call.
/// </para>
/// </remarks>
public sealed class PanelRestartRequest : IPanelRestart
{
    /// <summary>
    /// The exit status of a start that was asked for. Non-zero, because the service manager restarts the
    /// panel only after a failure exit; 75 is <c>EX_TEMPFAIL</c>, "try again", the conventional spelling
    /// of exactly this.
    /// </summary>
    public const int ExitStatus = 75;

    /// <summary>Asks the host to stop.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Opens the scope the audit write needs; this type is a singleton and the journal is not.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Whether a restart has been requested; read once, by <c>Program</c>, after the host stops.</summary>
    private volatile bool _requested;

    /// <summary>Whether a restart was requested, and the process should therefore exit with <see cref="ExitStatus"/>.</summary>
    public bool Requested
    {
        get { return _requested; }
    }

    /// <summary>Creates the request handler.</summary>
    /// <param name="lifetime">The host's lifetime, which is asked to stop.</param>
    /// <param name="scopeFactory">Opens the scope the audit write runs in.</param>
    public PanelRestartRequest(IHostApplicationLifetime lifetime, IServiceScopeFactory scopeFactory)
    {
        _lifetime = lifetime;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task RequestAsync(string moduleId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        // Audit first: a restart that happened must never be a restart nobody can find. If the write
        // fails the exception propagates and the panel keeps running - nothing was requested.
        using (var scope = _scopeFactory.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IModuleLoaderAudit>().RecordAsync(
                MarketplaceAuditActions.ModuleRestartRequested,
                moduleId,
                null,
                null,
                true,
                cancellationToken);
        }

        _requested = true;
        _lifetime.StopApplication();
    }
}
