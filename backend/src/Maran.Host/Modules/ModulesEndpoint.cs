using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;

namespace Maran.Host.Modules;

/// <summary>
/// Publishes the module list the SPA builds its navigation and route guards from. Lives in the
/// Host because it describes composition, not a feature: it reports what
/// <see cref="ModuleRegistry"/> composed, each module's own declared licence tier, and both the
/// module's and the tier's display names resolved in the request's culture.
/// </summary>
public static class ModulesEndpoint
{
    /// <summary>Maps <c>GET /api/v1/modules</c>, listing modules with their state.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapModuleCatalogue(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/v1/modules",
            (IErrorTextProvider errorTextProvider, LicenceTierDisplayNames tierDisplayNames, ICurrentUser currentUser) =>
                Results.Ok(DescribeModules(errorTextProvider, tierDisplayNames, currentUser)))
            // Anonymous: the SPA reads the catalogue to build its navigation before anyone has
            // signed in, and to know whether a login screen is even the right thing to show. The
            // list names the modules installed on this server and nothing about its data. An
            // unauthenticated caller is offered only what is addressed to everyone (fail closed),
            // so it sees the same, customer-shaped list a signed-in customer does.
            .AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    /// Describes every composed module the caller is allowed to be offered. Tier and display name
    /// come from each module's own <c>Manifest</c>; the state comes from
    /// <see cref="ModuleStateResolver"/>, the one place that decides it, so wiring real licence data
    /// later changes that resolver and not the contract.
    /// </summary>
    /// <remarks>
    /// The audience filter below (<see cref="IsOffered"/>) is presentation, not
    /// enforcement: it decides what the SPA's navigation offers, nothing more. Every endpoint keeps
    /// its own authorization policy regardless of what this list contains, so a module left out
    /// here is still exactly as protected — or not — as its own handlers make it.
    /// </remarks>
    /// <param name="errorTextProvider">Resolves each module's <c>Manifest.DisplayNameKey</c> in the current request culture.</param>
    /// <param name="tierDisplayNames">Resolves each module's licence tier into the words an operator reads.</param>
    /// <param name="currentUser">The caller, whose <see cref="ICurrentUser.Caller"/> case decides which module audiences are offered.</param>
    /// <returns>One descriptor per compiled-in module addressed to this caller, in registration order.</returns>
    private static List<ModuleDto> DescribeModules(
        IErrorTextProvider errorTextProvider,
        LicenceTierDisplayNames tierDisplayNames,
        ICurrentUser currentUser)
    {
        return ModuleRegistry.All
            .Where(module =>
            {
                return IsOffered(currentUser.Caller, module.Manifest.Audience);
            })
            .Select(module =>
            {
                return new ModuleDto(
                                module.Name,
                                module.Manifest.Tier,
                                tierDisplayNames.Of(module.Manifest.Tier),
                                errorTextProvider.Resolve(module.Manifest.DisplayNameKey),
                                ModuleStateResolver.Of(module.Manifest.Tier),
                                module.Manifest.AgentCapabilities);
            })
            .ToList();
    }

    /// <summary>Whether a module addressed to an audience is offered to a caller.</summary>
    /// <remarks>
    /// Every case of <see cref="Caller"/> is named, with no discard arm that would offer or hide a
    /// module to a case added later: an administrator is offered everything, a reseller what is
    /// addressed to everyone or to administrators and resellers, an API key nothing (it is not a login
    /// and has no navigation), and everyone else only what is addressed to everyone. Presentation only; the endpoints behind each module refuse regardless.
    /// </remarks>
    /// <param name="caller">The caller the catalogue is built for.</param>
    /// <param name="audience">The audience the module declares.</param>
    /// <returns><c>true</c> when the module belongs in the caller's navigation.</returns>
    private static bool IsOffered(Caller caller, ModuleAudience audience)
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
