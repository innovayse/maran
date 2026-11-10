using Maran.Host.Dashboard;
using Maran.Host.HealthChecks;
using Maran.Host.Modules;
using Maran.Host.Search;

namespace Maran.Host.Extensions;

/// <summary>
/// Maps every endpoint the panel serves: the minimal endpoints and the controller actions. It is the ONE
/// list, called by the serving host and by the <c>openapi</c> verb alike, so the document describes the
/// endpoints the running panel has and cannot drift from them.
/// </summary>
public static class PanelEndpointExtensions
{
    /// <summary>Maps the health probe, the module catalogue and frames, the dashboard, the search and the controllers.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPanelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPanelHealth();
        endpoints.MapModuleCatalogue();
        endpoints.MapModuleFrames();
        endpoints.MapDashboard();
        endpoints.MapDashboardStream();
        endpoints.MapAttentionCount();
        endpoints.MapFirstRun();
        endpoints.MapSearch();
        endpoints.MapControllers();
        return endpoints;
    }
}
