using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Finds the sites that stop an app from being removed: for a web server any site, for a PHP version the
/// sites on it, for a Node.js version the sites on it (issue #517). The one place that question is asked of
/// the Sites module, through <see cref="ISiteRuntimeUsage"/>.
/// </summary>
/// <remarks>
/// <para>
/// The panel checks FIRST and answers <c>AppsInUse</c> with the list, because a refusal that names the
/// sites tells the administrator what to move; the agent's own check of what is on disk
/// (<c>AgentAppInUse</c>) stays as the second line for a panel whose rows disagree with the host.
/// </para>
/// <para>
/// The list is capped, with the true total beside it: an administrator removing the web server of a host
/// with ten thousand sites needs the number and a screenful of examples, not a response the size of the
/// sites table.
/// </para>
/// </remarks>
public sealed class AppUsageGuard
{
    /// <summary>How many domains the refusal names; the total is always stated.</summary>
    public const int ListedDomains = 50;

    /// <summary>The Sites module's window onto which sites use which runtime.</summary>
    private readonly ISiteRuntimeUsage _sites;

    /// <summary>Creates the guard.</summary>
    /// <param name="sites">The Sites module's window onto which sites use which runtime.</param>
    public AppUsageGuard(ISiteRuntimeUsage sites)
    {
        _sites = sites;
    }

    /// <summary>Lists the sites that use <paramref name="appId"/>.</summary>
    /// <param name="appId">The app about to be removed.</param>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>The users, or <see langword="null"/> when no site uses the app.</returns>
    public async Task<AppInUseDto?> FindUsersAsync(AppId appId, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> domains = appId.Family switch
        {
            AppFamily.Php => await _sites.ListDomainsOnPhpAsync(appId.Version, cancellationToken),
            AppFamily.Node => await _sites.ListDomainsOnNodeAsync(appId.Version, cancellationToken),
            _ => await _sites.ListDomainsOnWebServerAsync(cancellationToken),
        };

        if (domains.Count == 0)
        {
            return null;
        }

        return new AppInUseDto(domains.Count, domains.Take(ListedDomains).ToList());
    }
}
