using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Resources;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Maran.Sdk.Filters;

/// <summary>
/// Refuses a request for a module the installed licence does not cover
/// (<see cref="RequiresLicensedModuleAttribute"/>). This is the boundary; the module catalogue only
/// advises the interface, and asks the same window, so the two cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who it applies to: everyone.</b> The licence binds the server, not the role, so an administrator
/// is refused exactly as a customer is, with the same code. Authentication and the endpoint's own
/// authorization policy run first, so an unauthenticated call is still a 401 and never learns which
/// modules are licensed.
/// </para>
/// <para>
/// <b>Fail closed, and only here.</b> The filter sits on the controllers of paid modules alone. When
/// the window answers anything but <see cref="EntitlementVerdict.Covered"/>, when it throws, or when
/// no window is registered at all, the paid request is refused (403 <c>ModuleLicenceRequired</c>); sign-in,
/// the health endpoints, the licence status and install endpoints, backups and the <c>maran</c> command
/// line are on no such controller and are never reached by this failure.
/// </para>
/// <para>
/// The window is asked through the request's services at run time, the Sdk's second sanctioned service
/// location (the first is the idempotency store): a constructor dependency would make every paid
/// controller unresolvable on a host that composes no Licensing module, and the right answer there is
/// a refusal, not a startup failure.
/// </para>
/// </remarks>
public sealed class RequiresLicensedModuleFilter : IAsyncActionFilter
{
    /// <summary>The module whose licence cover the request needs.</summary>
    private readonly string _moduleId;

    /// <summary>Creates the filter; the module id comes from the attribute.</summary>
    /// <param name="moduleId">The module's <see cref="Manifest.Id"/>.</param>
    public RequiresLicensedModuleFilter(string moduleId)
    {
        _moduleId = moduleId;
    }

    /// <summary>Lets the action run when the licence covers the module, otherwise refuses it.</summary>
    /// <param name="context">The action about to run; its result is set to the refusal.</param>
    /// <param name="next">Runs the action.</param>
    /// <returns>A task that completes when the action has run or the refusal has been set.</returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (await IsCoveredAsync(context.HttpContext))
        {
            await next();
            return;
        }

        var refusal = Error.Of(nameof(ValidationMessages.ModuleLicenceRequired), ErrorType.Forbidden);
        context.Result = Result.Fail(refusal).ToActionResult(context.HttpContext);
    }

    /// <summary>Asks the licence window; any failure to ask is "not covered".</summary>
    /// <param name="httpContext">The request, whose services hold the window.</param>
    /// <returns>True only when the window answers <see cref="EntitlementVerdict.Covered"/>.</returns>
    private async Task<bool> IsCoveredAsync(HttpContext httpContext)
    {
        var entitlements = httpContext.RequestServices.GetService<ILicenceEntitlements>();
        if (entitlements is null)
        {
            return false;
        }

        try
        {
            var entitlement = await entitlements.GetAsync(_moduleId, httpContext.RequestAborted);

            return entitlement.Verdict == EntitlementVerdict.Covered;
        }
        catch (Exception)
        {
            // The licence window is documented never to throw; a window that does is a bug in a
            // module the gate cannot see into, and the only safe answer for a paid module is "no".
            return false;
        }
    }
}
