namespace Maran.Host.Modules;

/// <summary>
/// Publishes the module list the SPA builds its navigation and route guards from. Lives in the
/// Host because it describes composition, not a feature: it reports what
/// <see cref="ModuleRegistry"/> composed, each module's own declared licence tier, and its
/// display name resolved in the request's culture.
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
            (IErrorTextProvider errorTextProvider) => Results.Ok(DescribeModules(errorTextProvider)))
            // Anonymous: the SPA reads the catalogue to build its navigation before anyone has
            // signed in, and to know whether a login screen is even the right thing to show. The
            // list names the modules installed on this server and nothing about its data.
            .AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    /// Describes every composed module. Until the Licensing module lands there is nothing to
    /// disable, so every entry reports an enabled state of true; tier and display name already
    /// come from each module's own <c>Manifest</c>, so wiring real licence data later only changes
    /// <c>IsEnabled</c>, not the contract.
    /// </summary>
    /// <param name="errorTextProvider">Resolves each module's <c>Manifest.DisplayNameKey</c> in the current request culture.</param>
    /// <returns>One descriptor per compiled-in module, in registration order.</returns>
    private static List<ModuleDto> DescribeModules(IErrorTextProvider errorTextProvider)
    {
        return ModuleRegistry.All
            .Select(module =>
            {
                return new ModuleDto(
                                module.Name,
                                module.Manifest.Tier,
                                errorTextProvider.Resolve(module.Manifest.DisplayNameKey),
                                IsEnabled: true);
            })
            .ToList();
    }
}
