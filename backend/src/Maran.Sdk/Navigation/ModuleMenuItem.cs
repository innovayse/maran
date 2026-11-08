using Maran.Sdk.Contracts;

namespace Maran.Sdk.Navigation;

/// <summary>
/// One entry a module contributes to the panel's navigation, as data: the SPA draws what the catalogue
/// returns and keeps no menu of its own for the module.
/// </summary>
/// <param name="Key">
/// Stable key, unique across every module in the panel (the SPA keys the entry's open state and its
/// jump-to-page palette by it). A module names its keys under its own id.
/// </param>
/// <param name="RouteName">The <see cref="ModuleRoute.Name"/> the entry opens.</param>
/// <param name="IconKey">A key of <see cref="ModuleIconKeys"/>; the shell draws a neutral glyph for an unknown one.</param>
/// <param name="LabelKey">
/// The resx key of the entry's label, resolved by the backend in the request culture; the module's
/// <c>Resources/DisplayNames*.resx</c> supplies the text in English, Russian and Armenian.
/// </param>
/// <param name="Order">Position among the entries offered to one caller, ascending; ties break by <paramref name="Key"/>.</param>
/// <param name="Audience">
/// Who is offered the entry. Presentation only, exactly as <see cref="Manifest.Audience"/> is: every
/// endpoint behind the screen enforces its caller's role on its own.
/// </param>
public sealed record ModuleMenuItem(
    string Key,
    string RouteName,
    string IconKey,
    string LabelKey,
    int Order,
    ModuleAudience Audience);
