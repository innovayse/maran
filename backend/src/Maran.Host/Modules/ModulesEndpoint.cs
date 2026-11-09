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
            async (
                IErrorTextProvider errorTextProvider,
                LicenceTierDisplayNames tierDisplayNames,
                ModuleKindDisplayNames kindDisplayNames,
                ICurrentUser currentUser,
                RuntimeModuleRegistry registry,
                CancellationToken cancellationToken) =>
                Results.Ok(await DescribeModulesAsync(
                    errorTextProvider, tierDisplayNames, kindDisplayNames, currentUser, registry, cancellationToken)))
            // Anonymous: the SPA reads the catalogue to build its navigation before anyone has
            // signed in, and to know whether a login screen is even the right thing to show. The
            // list names the modules installed on this server and nothing about its data. An
            // unauthenticated caller is offered only what is addressed to everyone (fail closed),
            // so it sees the same, customer-shaped list a signed-in customer does.
            .AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    /// Describes every composed module the caller is allowed to be offered - the modules that ship with
    /// the panel and the packaged ones composed at this start. Tier and display name come from each
    /// module's own <c>Manifest</c>; the state comes from <see cref="ModuleStateResolver"/>, the one
    /// place that decides it, from the tier and from the lifecycle state the runtime registry reports,
    /// so wiring real licence data later changes that resolver and not the contract. A package that is
    /// installed but not yet composed, or that was refused, is not offered: there is nothing to
    /// navigate to, and the install screen reads the loader's own list.
    /// </summary>
    /// <remarks>
    /// The audience filter below (<see cref="ModuleAudienceRule"/>) is presentation, not
    /// enforcement: it decides what the SPA's navigation offers, nothing more. Every endpoint keeps
    /// its own authorization policy regardless of what this list contains, so a module left out
    /// here is still exactly as protected — or not — as its own handlers make it.
    /// </remarks>
    /// <param name="errorTextProvider">Resolves each module's <c>Manifest.DisplayNameKey</c> in the current request culture.</param>
    /// <param name="tierDisplayNames">Resolves each module's licence tier into the words an operator reads.</param>
    /// <param name="kindDisplayNames">Resolves each module's kind into the words an operator reads.</param>
    /// <param name="currentUser">The caller, whose <see cref="ICurrentUser.Caller"/> case decides which module audiences are offered.</param>
    /// <param name="registry">The composed modules and the state the loader reports for the packaged ones.</param>
    /// <param name="cancellationToken">Cancellation token for the loader's list.</param>
    /// <returns>One descriptor per composed module addressed to this caller, in registration order.</returns>
    private static async Task<List<ModuleDto>> DescribeModulesAsync(
        IErrorTextProvider errorTextProvider,
        LicenceTierDisplayNames tierDisplayNames,
        ModuleKindDisplayNames kindDisplayNames,
        ICurrentUser currentUser,
        RuntimeModuleRegistry registry,
        CancellationToken cancellationToken)
    {
        var caller = currentUser.Caller;

        // First entry per id: composed modules are listed before refused ones, and a refused package may
        // carry the id of a module that is composed (that is one of the ways it is refused), so the
        // composed module's own state must be the one that wins.
        var lifecycle = (await registry.ListAsync(cancellationToken))
            .GroupBy(registered => { return registered.Id; }, StringComparer.Ordinal)
            .ToDictionary(
                group => { return group.Key; },
                group => { return group.First().State; },
                StringComparer.Ordinal);

        return registry.Modules
            .Where(module =>
            {
                return ModuleAudienceRule.IsOffered(caller, module.Manifest.Audience);
            })
            .Select(module =>
            {
                return new ModuleDto(
                    module.Name,
                    module.Manifest.Tier,
                    tierDisplayNames.Of(module.Manifest.Tier),
                    errorTextProvider.Resolve(module.Manifest.DisplayNameKey),
                    ModuleStateResolver.Of(module.Manifest.Tier, lifecycle[module.Manifest.Id]),
                    module.Manifest.AgentCapabilities,
                    module.Manifest.MinPanelVersion,
                    module.Manifest.Kind,
                    kindDisplayNames.Of(module.Manifest.Kind),
                    module.Manifest.Permissions,
                    ModuleMenuLocalizer.MenuOf(module.Manifest, caller, errorTextProvider),
                    ModuleMenuLocalizer.RoutesOf(module.Manifest, errorTextProvider));
            })
            .ToList();
    }
}
