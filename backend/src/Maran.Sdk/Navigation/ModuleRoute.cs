namespace Maran.Sdk.Navigation;

/// <summary>
/// One screen of a module's interface: the name and path the SPA registers it under.
/// </summary>
/// <param name="Name">The route's name, unique across every module in the panel.</param>
/// <param name="Path">The route's path inside the SPA, beginning with <c>/</c>.</param>
/// <param name="TitleKey">
/// The resx key of the screen's title, resolved by the backend in the request culture, the same way
/// <see cref="ModuleMenuItem.LabelKey"/> is.
/// </param>
/// <param name="RequiresModule">
/// The id of ANOTHER module that must be enabled for this screen to be usable, or null when the
/// screen needs nothing beyond the module that declares it. It is never the declaring module's own id.
/// </param>
public sealed record ModuleRoute(string Name, string Path, string TitleKey, string? RequiresModule);
