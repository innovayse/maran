namespace Maran.Host.Modules;

/// <summary>One screen route a module contributes, with its title already localized.</summary>
/// <param name="Name">Stable machine name of the route.</param>
/// <param name="Path">The route's path inside the SPA.</param>
/// <param name="Title">The route's title in the request's culture, resolved from the module's own resources.</param>
/// <param name="RequiresModule">The id of another module that must be enabled for the screen to be usable, or null when it needs none.</param>
public sealed record ModuleRouteDto(string Name, string Path, string Title, string? RequiresModule);
