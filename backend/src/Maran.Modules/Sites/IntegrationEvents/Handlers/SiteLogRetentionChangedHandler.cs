using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.IntegrationEvents.Handlers;

/// <summary>
/// Applies a changed log retention to the one site it was changed on, without waiting for the next hourly
/// pass (<see cref="SiteLogRetentionChanged"/>).
/// </summary>
/// <remarks>
/// It runs the same per-site maintenance the hourly pass does, so the read still comes before the prune.
/// The message is published after the commit and may be lost; the hourly pass converges every site to its
/// stored number anyway, which is the guarantee the publisher relies on. A refusal is logged by the
/// maintainer and not retried here, for the same reason.
/// </remarks>
public sealed class SiteLogRetentionChangedHandler
{
    /// <summary>Maintains one site at a time.</summary>
    private readonly SiteLogMaintainer _maintainer;

    /// <summary>Creates the handler.</summary>
    /// <param name="maintainer">Maintains one site at a time.</param>
    public SiteLogRetentionChangedHandler(SiteLogMaintainer maintainer)
    {
        _maintainer = maintainer;
    }

    /// <summary>Reads and then prunes the site whose retention changed.</summary>
    /// <param name="message">The site whose retention changed.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>Resolves when the site has been maintained.</returns>
    public async Task HandleAsync(SiteLogRetentionChanged message, CancellationToken cancellationToken)
    {
        await _maintainer.MaintainAsync(message.SiteId, cancellationToken);
    }
}
