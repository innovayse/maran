using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

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
                IModuleSpaAssets spaAssets,
                ILicenceEntitlements entitlements,
                CancellationToken cancellationToken) =>
                Results.Ok(await DescribeModulesAsync(
                    errorTextProvider,
                    tierDisplayNames,
                    kindDisplayNames,
                    currentUser,
                    registry,
                    spaAssets,
                    entitlements,
                    cancellationToken)))
            // Anonymous: the SPA reads the catalogue to build its navigation before anyone has
            // signed in, and to know whether a login screen is even the right thing to show. The
            // list names the modules installed on this server and nothing about its data. An
            // unauthenticated caller is offered only what is addressed to everyone (fail closed),
            // so it sees the same, customer-shaped list a signed-in customer does.
            .AllowAnonymous()
            .Produces<List<ModuleDto>>(StatusCodes.Status200OK);
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
    /// <param name="spaAssets">The window onto the screen bundles of packaged modules.</param>
    /// <param name="entitlements">The licence window: one read answers for every paid module in the list.</param>
    /// <param name="cancellationToken">Cancellation token for the loader's list.</param>
    /// <returns>One descriptor per composed module addressed to this caller, in registration order.</returns>
    private static async Task<List<ModuleDto>> DescribeModulesAsync(
        IErrorTextProvider errorTextProvider,
        LicenceTierDisplayNames tierDisplayNames,
        ModuleKindDisplayNames kindDisplayNames,
        ICurrentUser currentUser,
        RuntimeModuleRegistry registry,
        IModuleSpaAssets spaAssets,
        ILicenceEntitlements entitlements,
        CancellationToken cancellationToken)
    {
        var caller = currentUser.Caller;

        // First entry per id: composed modules are listed before refused ones, and a refused package may
        // carry the id of a module that is composed (that is one of the ways it is refused), so the
        // composed module's own state must be the one that wins.
        var registered = (await registry.ListAsync(cancellationToken))
            .GroupBy(entry => { return entry.Id; }, StringComparer.Ordinal)
            .ToDictionary(group => { return group.Key; }, group => { return group.First(); }, StringComparer.Ordinal);

        var offered = registry.Modules
            .Where(module => { return ModuleAudienceRule.IsOffered(caller, module.Manifest.Audience); })
            .ToList();

        // One read for the whole list, and only the paid modules are asked about: an included module
        // consults no licence, so a panel whose modules are all included never touches the licence here.
        var paidIds = offered
            .Where(module => { return module.Manifest.Tier != LicenceTier.Included; })
            .Select(module => { return module.Manifest.Id; })
            .ToList();
        var verdicts = (await entitlements.ListAsync(paidIds, cancellationToken))
            .ToDictionary(entitlement => { return entitlement.ModuleId; }, StringComparer.Ordinal);

        var described = new List<ModuleDto>();
        foreach (var module in offered)
        {
            verdicts.TryGetValue(module.Manifest.Id, out var entitlement);
            var verdict = entitlement?.Verdict ?? EntitlementVerdict.NotCovered;

            var entry = registered[module.Manifest.Id];
            var isPackaged = registry.IsPackaged(module);
            var bundle = ModuleFrameRule.Frame(
                module.Manifest,
                isPackaged,
                entry.State,
                isPackaged ? await spaAssets.DescribeAsync(module.Manifest.Id, cancellationToken) : null);

            described.Add(new ModuleDto(
                module.Name,
                module.Manifest.Tier,
                tierDisplayNames.Of(module.Manifest.Tier),
                errorTextProvider.Resolve(module.Manifest.DisplayNameKey),
                ModuleStateResolver.Of(module.Manifest.Tier, entry.State, verdict),
                module.Manifest.AgentCapabilities,
                module.Manifest.MinPanelVersion,
                module.Manifest.Kind,
                kindDisplayNames.Of(module.Manifest.Kind),
                module.Manifest.Permissions,
                ModuleMenuLocalizer.MenuOf(module.Manifest, caller, errorTextProvider),
                ModuleMenuLocalizer.RoutesOf(module.Manifest, errorTextProvider),
                module.Manifest.Version,
                entry.State,
                isPackaged,
                bundle is null ? null : SpaOf(module.Manifest, bundle),
                FailureDisplayOf(entry, errorTextProvider),
                entitlement?.ValidUntil));
        }

        return described;
    }

    /// <summary>Describes the frame of a module the panel will frame.</summary>
    /// <param name="manifest">The composed module's manifest, whose id and version build the frame's address.</param>
    /// <param name="bundle">The bundle <see cref="ModuleFrameRule"/> decided to serve.</param>
    /// <returns>The address, integrity value and locales the shell needs.</returns>
    private static ModuleSpaDto SpaOf(Manifest manifest, ModuleSpaDescription bundle)
    {
        return new ModuleSpaDto(ModuleFrameRule.FrameUrlOf(manifest.Id, manifest.Version), bundle.Integrity, bundle.Locales);
    }

    /// <summary>Resolves the sentence for a degraded or failed module in the request's culture.</summary>
    /// <param name="entry">The module as the registry lists it.</param>
    /// <param name="errorTextProvider">Resolves the loader's coded cause.</param>
    /// <returns>
    /// The sentence, or null when the module is neither degraded nor failed, the loader stated no cause, or no
    /// loaded table has a sentence for it - the machine code is never shown in its place.
    /// </returns>
    private static string? FailureDisplayOf(RegisteredModule entry, IErrorTextProvider errorTextProvider)
    {
        if (entry.State is not (ModuleLifecycleState.Degraded or ModuleLifecycleState.Failed) || entry.Failure is null)
        {
            return null;
        }

        return errorTextProvider.TryResolve(entry.Failure.Code, out var text) ? text : null;
    }
}
