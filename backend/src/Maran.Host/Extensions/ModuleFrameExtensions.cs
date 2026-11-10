using Maran.Host.Modules;
using Maran.Host.RateLimiting;

namespace Maran.Host.Extensions;

/// <summary>Maps the route that serves the screen bundles of packaged modules to their sandboxed frames.</summary>
public static class ModuleFrameExtensions
{
    /// <summary>Maps <c>GET /module-frames/{id}/{version}/{**path}</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapModuleFrames(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ModuleFrameRule.RouteTemplate, ModuleFrameEndpoint.ServeAsync)
            // Anonymous: the frame is sandboxed with an opaque origin, so the browser sends it no
            // cookie and no token. It serves signed, non-secret files of an active module and nothing
            // that belongs to a tenant; the data a module shows travels over the shell's bridge.
            .AllowAnonymous()
            // Keyed by address, because the caller is anonymous.
            .RequireRateLimiting(ApiRateLimitPolicy.Name)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            // Static screen files for a sandboxed frame, not an operation of the panel API: the catch-all
            // path has no document form, and a file route has no contract for a client to generate from.
            .ExcludeFromDescription();
        return endpoints;
    }
}
