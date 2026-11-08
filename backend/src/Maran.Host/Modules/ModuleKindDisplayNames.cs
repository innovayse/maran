using System.Text.Json;
using Maran.Host.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Host.Modules;

/// <summary>
/// Resolves the operator-facing name of a <see cref="ModuleKind"/> in the current request's
/// culture, so the module catalogue can carry the kind's words beside its machine value.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same arrangement as <see cref="LicenceTierDisplayNames"/>, for the same reason.</b> The
/// kind is declared in the Sdk's module contract and reported by <see cref="ModulesEndpoint"/>; it
/// belongs to no module, so its words are the Host's own <c>Resources/DisplayNames*.resx</c>, and
/// the panel keeps one way of naming a machine-stable value.
/// </para>
/// <para>
/// <b>The fallback is the wire spelling, and the census test is what makes it unreachable.</b> A
/// member added to <see cref="ModuleKind"/> without a resx entry resolves to the camelCase constant
/// the row's <c>kind</c> field carries; <c>ModuleKindDisplayNamesTests</c> walks the enum and fails
/// on the first member whose answer is that spelling.
/// </para>
/// </remarks>
public sealed class ModuleKindDisplayNames
{
    /// <summary>The prefix a kind's resx key carries, so kinds cannot collide with other names.</summary>
    private const string KeyPrefix = "ModuleKind";

    /// <summary>Resolves the Host's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">The Host's display-name resources.</param>
    public ModuleKindDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one module kind as an operator reads it.</summary>
    /// <param name="kind">The machine-stable kind a module declared in its manifest.</param>
    /// <returns>
    /// The localized name, or the member's camelCase wire spelling when this build carries no entry
    /// for it, which the census test over the enum keeps from happening for anything that ships.
    /// </returns>
    public string Of(ModuleKind kind)
    {
        var localized = _displayNames[KeyPrefix + kind];

        // The panel-wide enum converter's own naming policy, so the degraded name is byte-for-byte
        // the machine constant the row's `kind` field carries on the wire.
        return localized.ResourceNotFound
            ? JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())
            : localized.Value;
    }
}
