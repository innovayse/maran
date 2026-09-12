namespace Maran.Host.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). It carries the user-facing names of the
/// vocabulary the COMPOSITION ROOT owns rather than any module: one
/// <c>LicenceTier&lt;Member&gt;</c> entry per <see cref="Maran.Sdk.Contracts.LicenceTier"/> member,
/// resolved by <see cref="Modules.LicenceTierDisplayNames"/> for the module catalogue.
/// </summary>
/// <remarks>
/// Separate from <see cref="ErrorMessages"/> on purpose, one file per purpose (rules/csharp.md):
/// that table holds the two failures the host itself produces and is reached by error code through
/// <see cref="Maran.SharedKernel.Interfaces.IErrorTextProvider"/>; this one holds names of things
/// that are not failures at all, and mixing them would make "which table does this key belong in"
/// a question with no answer.
/// </remarks>
public sealed class DisplayNames
{
}
