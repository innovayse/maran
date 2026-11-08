using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;

namespace Maran.Host.Modules;

/// <summary>
/// The one rule that says whether something addressed to a <see cref="ModuleAudience"/> belongs in a
/// caller's navigation. A module as a whole and each of its menu entries are judged by it, so the two
/// can never drift into offering different things to the same caller.
/// </summary>
/// <remarks>
/// Presentation only: the endpoints behind a module refuse regardless of what the catalogue lists
/// (rules/architecture.md "Where a module's UI lives").
/// </remarks>
public static class ModuleAudienceRule
{
    /// <summary>Whether something addressed to an audience is offered to a caller.</summary>
    /// <remarks>
    /// Every case of <see cref="Caller"/> is named, with no discard arm that would offer or hide a
    /// module to a case added later: an administrator is offered everything, a reseller what is
    /// addressed to everyone or to administrators and resellers, an API key nothing (it is not a login
    /// and has no navigation), and everyone else only what is addressed to everyone.
    /// </remarks>
    /// <param name="caller">The caller the catalogue is built for.</param>
    /// <param name="audience">The audience the module or the menu entry declares.</param>
    /// <returns><c>true</c> when it belongs in the caller's navigation.</returns>
    public static bool IsOffered(Caller caller, ModuleAudience audience)
    {
        return caller switch
        {
            AdministratorCaller => true,
            ResellerCaller => audience is ModuleAudience.Everyone or ModuleAudience.AdminOrReseller,
            CustomerCaller => audience == ModuleAudience.Everyone,
            AccountlessCustomerCaller => audience == ModuleAudience.Everyone,
            ApiKeyCaller => false,
            AnonymousCaller => audience == ModuleAudience.Everyone,
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
        };
    }
}
