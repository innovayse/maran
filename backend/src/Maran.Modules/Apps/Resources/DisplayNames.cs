namespace Maran.Modules.Apps.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Apps module
/// owns: <c>AppsModuleDisplayName</c>, resolved via <see cref="AppsManifest"/>'s <c>DisplayNameKey</c>,
/// the labels of the closed sets the store screens select from (<c>AppKind*</c>, <c>AppState*</c>,
/// <c>AppServiceStatus*</c>), the format of each app family's name (<c>AppFamily*</c>), the sentence for each reason Node.js is missing from
/// the installable list (<c>NodejsUnavailable*</c>), and the labels of
/// the module's menu entry (<c>AppsMenu*</c>) and route (<c>AppsRoute*</c>).
/// </summary>
public sealed class DisplayNames
{
}
