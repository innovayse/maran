using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;

namespace Maran.Host.Modules;

/// <summary>
/// Turns a module's declared menu entries and routes into what the catalogue sends: text resolved in
/// the request's culture, and only the entries the caller is offered.
/// </summary>
/// <remarks>
/// The module declares resource KEYS and never words (rules/architecture.md "The backend owns the
/// data, the SPA renders it"). The audience judgement is <see cref="ModuleAudienceRule"/>, the same
/// one the catalogue applies to the module itself.
/// </remarks>
public static class ModuleMenuLocalizer
{
    /// <summary>Lists the menu entries of a module that the caller is offered, localized and ordered.</summary>
    /// <param name="manifest">The module's manifest.</param>
    /// <param name="caller">The caller the catalogue is built for.</param>
    /// <param name="errorTextProvider">Resolves each entry's <c>LabelKey</c> in the current request culture.</param>
    /// <returns>The entries ordered by their declared order, then by key.</returns>
    public static IReadOnlyList<ModuleMenuItemDto> MenuOf(
        Manifest manifest,
        Caller caller,
        IErrorTextProvider errorTextProvider)
    {
        return manifest.Menu
            .Where(item =>
            {
                return ModuleAudienceRule.IsOffered(caller, item.Audience);
            })
            .OrderBy(item => { return item.Order; })
            .ThenBy(item => { return item.Key; }, StringComparer.Ordinal)
            .Select(item =>
            {
                return new ModuleMenuItemDto(
                    item.Key,
                    item.RouteName,
                    item.IconKey,
                    errorTextProvider.Resolve(item.LabelKey),
                    item.Order);
            })
            .ToList();
    }

    /// <summary>Lists the routes of a module, with their titles localized.</summary>
    /// <param name="manifest">The module's manifest.</param>
    /// <param name="errorTextProvider">Resolves each route's <c>TitleKey</c> in the current request culture.</param>
    /// <returns>The routes in the order the module declared them.</returns>
    public static IReadOnlyList<ModuleRouteDto> RoutesOf(Manifest manifest, IErrorTextProvider errorTextProvider)
    {
        return manifest.Routes
            .Select(route =>
            {
                return new ModuleRouteDto(
                    route.Name,
                    route.Path,
                    errorTextProvider.Resolve(route.TitleKey),
                    route.RequiresModule);
            })
            .ToList();
    }
}
