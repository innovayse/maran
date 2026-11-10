using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// What the host's installed apps allow a site to be: whether sites can exist at all, which web server
/// serves them, and which runtimes they may bind to. Read once per request from the Apps module's
/// <c>IStackStatus</c> by <see cref="SiteStackGate"/>.
/// </summary>
/// <param name="IsReady">Whether a site web server is installed. While false, no site operation may run.</param>
/// <param name="WebServer">The installed site web server, or null when none is installed.</param>
/// <param name="PhpVersions">The installed PHP versions, e.g. <c>8.3</c>.</param>
/// <param name="NodeMajors">The installed Node majors, e.g. <c>22</c>.</param>
public sealed record SiteHostApps(
    bool IsReady,
    SiteWebServer? WebServer,
    IReadOnlyList<string> PhpVersions,
    IReadOnlyList<string> NodeMajors)
{
    /// <summary>The refusal every site-mutating operation answers with while no site web server is installed.</summary>
    /// <returns>The failure when the stack is not ready; otherwise null.</returns>
    public Error? NotReadyFailure()
    {
        return IsReady && WebServer is not null ? null : Error.Of(nameof(ErrorMessages.AppsStackNotReady), ErrorType.Conflict);
    }

    /// <summary>The refusal for a runtime that is not installed as an app.</summary>
    /// <returns>The failure carrying the same code the Apps module uses for an app that is not installed.</returns>
    public static Error NotInstalledFailure()
    {
        return Error.Of(nameof(ErrorMessages.AppsNotInstalled), ErrorType.Conflict);
    }

    /// <summary>Whether a PHP version is installed as an app.</summary>
    /// <param name="version">The two-component version.</param>
    /// <returns>True when it is installed.</returns>
    public bool HasPhp(string version)
    {
        return PhpVersions.Contains(version, StringComparer.Ordinal);
    }

    /// <summary>Whether a Node major is installed as an app.</summary>
    /// <param name="major">The Node major.</param>
    /// <returns>True when it is installed.</returns>
    public bool HasNode(string major)
    {
        return NodeMajors.Contains(major, StringComparer.Ordinal);
    }
}
