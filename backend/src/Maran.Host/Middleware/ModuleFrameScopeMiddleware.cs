using Maran.Host.Modules;
using Maran.Sdk.Extensions;
using Maran.Sdk.Manifests;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Maran.Host.Middleware;

/// <summary>
/// Holds a call the shell relays for a module's screen to that module's own endpoints. When a request
/// carries <see cref="HeaderName"/> naming a module, the endpoint it matched must belong to that module
/// (<see cref="ModuleEndpointOwners"/>); anything else is refused with <c>ModuleFrameOutOfScope</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The server decides; the shell only declares.</b> The shell adds the header to the requests it
/// performs for a frame, and the SPA also checks the path prefix, but that check is advice. A frame has no
/// network access of its own and cannot add or drop the header, so the declaration is the shell's; this
/// middleware is what makes it binding. A call with no header is a normal call and is not touched.
/// </para>
/// <para>
/// <b>Fails closed.</b> A header that is not exactly one well-formed module id, an endpoint no module
/// owns, and an owner other than the one named are all out of scope. A request that matched no endpoint
/// passes through unchanged and is answered 404 by the framework, as it would be without the header.
/// Registered after routing, authentication and authorization, so the matched endpoint is known and a
/// caller who may not call the endpoint at all is answered by the authorization system first.
/// </para>
/// <para>
/// The refusal is written by the Sdk's one translation of a failed <see cref="Result"/> into the panel's
/// problem response, so its shape is the shape of every other refusal.
/// </para>
/// </remarks>
public sealed class ModuleFrameScopeMiddleware
{
    /// <summary>The header the shell sets, with the id of the module whose screen the call is made for.</summary>
    public const string HeaderName = "X-Maran-Frame";

    /// <summary>Code and resource key of the refusal (status 403).</summary>
    public const string OutOfScopeCode = "ModuleFrameOutOfScope";

    /// <summary>The refusal every out-of-scope call is answered with.</summary>
    private static readonly Error OutOfScope = Error.Of(OutOfScopeCode, ErrorType.Forbidden);

    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>Which module owns which endpoint.</summary>
    private readonly ModuleEndpointOwners _owners;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="owners">Which module owns which endpoint.</param>
    public ModuleFrameScopeMiddleware(RequestDelegate next, ModuleEndpointOwners owners)
    {
        _next = next;
        _owners = owners;
    }

    /// <summary>Passes a call that is in scope, and refuses one that is not.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>Resolves once the request has been handled or refused.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var declared))
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        if (IsOwnedBy(endpoint, declared))
        {
            await _next(context);
            return;
        }

        var refusal = Result.Fail(OutOfScope).ToActionResult(context);
        await refusal.ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));
    }

    /// <summary>Whether the declared module is exactly one well-formed id and owns the endpoint.</summary>
    /// <param name="endpoint">The endpoint the request matched.</param>
    /// <param name="declared">The header's values.</param>
    /// <returns>True when the call is in scope.</returns>
    private bool IsOwnedBy(Endpoint endpoint, StringValues declared)
    {
        if (declared.Count != 1 || declared[0] is not { } moduleId || !ManifestValidator.IsWellFormedId(moduleId))
        {
            return false;
        }

        return string.Equals(_owners.OwnerOf(endpoint), moduleId, StringComparison.Ordinal);
    }
}
