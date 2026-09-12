using System.Text.Json;
using Maran.Host.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Host.Modules;

/// <summary>
/// Resolves the operator-facing name of a <see cref="LicenceTier"/> in the current request's
/// culture, so the module catalogue can carry the tier's words beside its machine value.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is the backend's to produce, not the SPA's.</b> Plan and tier names are named
/// explicitly in rules/vue.md as server-side text the SPA renders rather than owns ("no
/// client-side lists of plans, statuses, tiers or limits"), and the upgrade screen proved why:
/// it interpolated the raw contract constant into a translated sentence, so a Russian operator
/// read "Он доступен в тарифе addOn." Built in the same shape as the Tasks module's kind names,
/// the Backups module's failure and destination names, the Monitoring module's service names and
/// the Identity module's audit-action names — deliberately, so the panel keeps ONE way of naming a
/// machine-stable value rather than a sixth.
/// </para>
/// <para>
/// <b>It lives in the Host, not in a module, because the catalogue does.</b> The tier is declared
/// in the Sdk's module contract and reported by <see cref="ModulesEndpoint"/>; no module may read
/// another's resources, and a licence tier belongs to none of them. Its resx family is therefore
/// the Host's own <c>Resources/DisplayNames*.resx</c>.
/// </para>
/// <para>
/// <b>The fallback is the wire spelling, and the census test is what makes it unreachable.</b> A
/// member added to <see cref="LicenceTier"/> without a resx entry resolves to the same camelCase
/// constant the upgrade screen printed before this type existed — never the resx key, which names
/// a file nobody outside this repository can read. The camelCase form is chosen because it is the
/// spelling the client already holds in the row's <c>tier</c> field, so a degraded name at least
/// matches the code beside it. <c>LicenceTierDisplayNamesTests</c> walks the enum and fails on the
/// first member whose answer is that spelling.
/// </para>
/// </remarks>
public sealed class LicenceTierDisplayNames
{
    /// <summary>The prefix a tier's resx key carries, so tiers cannot collide with other names.</summary>
    private const string KeyPrefix = "LicenceTier";

    /// <summary>Resolves the Host's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">The Host's display-name resources.</param>
    public LicenceTierDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one licence tier as an operator reads it.</summary>
    /// <param name="tier">The machine-stable tier a module declared in its manifest.</param>
    /// <returns>
    /// The localized name, or the member's camelCase wire spelling when this build carries no entry
    /// for it — which the census test over the enum keeps from happening for anything that ships.
    /// </returns>
    public string Of(LicenceTier tier)
    {
        var localized = _displayNames[KeyPrefix + tier];

        // The panel-wide enum converter's own naming policy, so the degraded name is byte-for-byte
        // the machine constant the row's `tier` field carries on the wire.
        return localized.ResourceNotFound
            ? JsonNamingPolicy.CamelCase.ConvertName(tier.ToString())
            : localized.Value;
    }
}
