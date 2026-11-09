namespace Maran.Host.Modules;

/// <summary>
/// One navigation entry a module contributes, already localized and already filtered to what the
/// caller is offered. The SPA renders it and owns no word of it.
/// </summary>
/// <param name="Key">Stable machine key of the entry, unique across the panel.</param>
/// <param name="RouteName">Name of the module route the entry leads to (<see cref="ModuleRouteDto.Name"/>).</param>
/// <param name="IconKey">Key from the closed icon vocabulary; an unknown key renders a neutral glyph.</param>
/// <param name="Label">The entry's text in the request's culture, resolved from the module's own resources.</param>
/// <param name="Order">Position among the panel's entries; lower comes first.</param>
public sealed record ModuleMenuItemDto(string Key, string RouteName, string IconKey, string Label, int Order);
