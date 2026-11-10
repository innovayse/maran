using Maran.Agent.Client.Services.SitesService;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// The Sites module's one window onto the Apps module's <see cref="IStackStatus"/>: it translates what the
/// installed apps are into what a site may be, so that no handler reads the other module's types or
/// decides on its own what "ready" means.
/// </summary>
/// <remarks>
/// This is where the stack gate is enforced for sites (docs/apps/README.md): every site-mutating command
/// reads the stack here and refuses with <c>AppsStackNotReady</c> while no web server is installed. The
/// SPA only reflects the refusal (rules/security.md).
/// </remarks>
public sealed class SiteStackGate
{
    /// <summary>The Apps module's answer about what is installed.</summary>
    private readonly IStackStatus _stack;

    /// <summary>Creates the gate.</summary>
    /// <param name="stack">The Apps module's answer about what is installed.</param>
    public SiteStackGate(IStackStatus stack)
    {
        _stack = stack;
    }

    /// <summary>Reads what is installed right now.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The installed stack.</returns>
    public Task<SiteHostApps> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new SiteHostApps(
            _stack.IsReady,
            SiteWebServerMapper.From(_stack.WebServer),
            _stack.PhpVersions,
            _stack.NodeMajors));
    }

    /// <summary>Reads the installed site web server for a caller that has no request to refuse.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The installed site web server.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when none is installed; <see cref="SiteWebServerMapper.Require"/> says why that is a defect and not a refusal.
    /// </exception>
    public Task<SiteWebServer> RequireWebServerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(SiteWebServerMapper.Require(_stack));
    }
}
