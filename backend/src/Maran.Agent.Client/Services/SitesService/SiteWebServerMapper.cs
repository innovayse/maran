using Maran.SharedKernel.Apps;
using Maran.SharedKernel.Interfaces;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Maps the web server the app store reports onto the one the agent client speaks.</summary>
/// <remarks>
/// The one place the two closed sets meet, so every module that renders a vhost or reloads the web server
/// (Sites, Ssl) translates the installed web server the same way instead of each keeping its own switch.
/// </remarks>
public static class SiteWebServerMapper
{
    /// <summary>Maps the installed web server, or says that none is installed.</summary>
    /// <param name="installed">The app store's answer.</param>
    /// <returns>The matching web server, or null when none is installed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value the mapping does not know.</exception>
    public static SiteWebServer? From(StackWebServer installed)
    {
        return installed switch
        {
            StackWebServer.None => null,
            StackWebServer.Nginx => SiteWebServer.Nginx,
            StackWebServer.Apache => SiteWebServer.Apache,
            _ => throw new ArgumentOutOfRangeException(nameof(installed), installed, "Unmapped stack web server."),
        };
    }

    /// <summary>Maps the installed web server for a caller that only runs once a site exists.</summary>
    /// <param name="stack">The app store's answer about what is installed.</param>
    /// <returns>The installed site web server.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when none is installed. For a certificate install or a renewal reload that is a broken
    /// invariant, not a customer's mistake: uninstalling the web server is refused while a site exists, so
    /// a site with no web server means the ledger and the host disagree, and failing loudly beats
    /// rendering a vhost for a server nobody installed.
    /// </exception>
    public static SiteWebServer Require(IStackStatus stack)
    {
        return From(stack.WebServer)
            ?? throw new InvalidOperationException("A site exists but no site web server is installed.");
    }
}
