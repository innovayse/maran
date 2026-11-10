using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Commands.RestartForModule;

/// <summary>
/// Handles <see cref="RestartForModuleCommand"/>: asks the panel to restart so a staged module takes effect.
/// </summary>
/// <remarks>
/// <para>
/// The restart is the Sdk's <see cref="IPanelRestart"/>, which journals the request itself and does the
/// restart; this handler adds no second entry of its own, since the restart is the one thing it does and
/// the Sdk's entry already names the module. A module the loader does not list is
/// <c>ModuleNotInstalled</c> and nothing restarts. Asking twice is harmless: the second request meets the
/// panel already going down or restarted, and the administrator's screen polls the local catalogue until
/// the module appears.
/// </para>
/// </remarks>
public sealed class RestartForModuleCommandHandler
{
    /// <summary>The installed modules.</summary>
    private readonly InstalledModuleReader _installed;

    /// <summary>Restarts the panel.</summary>
    private readonly IPanelRestart _restart;

    /// <summary>Creates the handler.</summary>
    /// <param name="installed">The installed modules.</param>
    /// <param name="restart">Restarts the panel.</param>
    public RestartForModuleCommandHandler(InstalledModuleReader installed, IPanelRestart restart)
    {
        _installed = installed;
        _restart = restart;
    }

    /// <summary>Requests the restart.</summary>
    /// <param name="command">The validated command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success once the restart is requested, or <c>ModuleNotInstalled</c>.</returns>
    public async Task<Result> HandleAsync(RestartForModuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var known = (await _installed.ReadAsync(cancellationToken))
            .Any(facts => { return facts.Loaded.ModuleId == command.Id; });
        if (!known)
        {
            return Result.Fail(MarketplaceOperationRefusals.ModuleNotInstalled);
        }

        await _restart.RequestAsync(command.Id, cancellationToken);
        return Result.Ok();
    }
}
