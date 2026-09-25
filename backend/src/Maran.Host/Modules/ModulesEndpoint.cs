using Maran.Sdk.Contracts;

namespace Maran.Host.Modules;

/// <summary>
/// Publishes the module list the SPA builds its navigation and route guards from. Lives in the
/// Host because it describes composition, not a feature: it reports what
/// <see cref="ModuleRegistry"/> composed, each module's own declared licence tier, and both the
/// module's and the tier's display names resolved in the request's culture.
/// </summary>
public static class ModulesEndpoint
{
    /// <summary>Maps <c>GET /api/v1/modules</c>, listing modules with their licence state.</summary>
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
            // unauthenticated caller reads ICurrentUser.IsAdmin as false (fail closed), so it sees
            // the same, customer-shaped list a signed-in customer does.
            .AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    /// Describes every composed module the caller is allowed to be offered. Until the Licensing
    /// module lands there is nothing to disable, so every entry reports an enabled state of true;
    /// tier and display name already come from each module's own <c>Manifest</c>, so wiring real
    /// licence data later only changes <c>IsEnabled</c>, not the contract.
    /// </summary>
    /// <remarks>
    /// The <see cref="ModuleAudience.AdministratorOnly"/> filter below is presentation, not
    /// enforcement: it decides what the SPA's navigation offers, nothing more. Every endpoint keeps
    /// its own authorization policy regardless of what this list contains, so a module left out
    /// here is still exactly as protected — or not — as its own handlers make it.
    /// </remarks>
    /// <param name="errorTextProvider">Resolves each module's <c>Manifest.DisplayNameKey</c> in the current request culture.</param>
    /// <param name="tierDisplayNames">Resolves each module's licence tier into the words an operator reads.</param>
    /// <param name="currentUser">The caller, whose <see cref="ICurrentUser.IsAdmin"/> decides whether administrator-only modules are included.</param>
    /// <returns>One descriptor per compiled-in module addressed to this caller, in registration order.</returns>
    private static List<ModuleDto> DescribeModules(
        IErrorTextProvider errorTextProvider,
        LicenceTierDisplayNames tierDisplayNames,
        ICurrentUser currentUser)
    {
        return ModuleRegistry.All
            .Where(module =>
            {
                return currentUser.IsAdmin || module.Manifest.Audience == ModuleAudience.Everyone;
            })
            .Select(module =>
            {
                return new ModuleDto(
                                module.Name,
                                module.Manifest.Tier,
                                tierDisplayNames.Of(module.Manifest.Tier),
                                errorTextProvider.Resolve(module.Manifest.DisplayNameKey),
                                IsEnabled: true,
                                module.Manifest.AgentCapabilities);
            })
            .ToList();
    }
}
