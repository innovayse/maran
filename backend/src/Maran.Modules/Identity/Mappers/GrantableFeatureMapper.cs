using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Spells a feature on the wire: the feature and its localized display name. One place, because the
/// list of what an owner may grant and the list of what an offer would grant name features the same way.
/// </summary>
public static class GrantableFeatureMapper
{
    /// <summary>The prefix of a feature's key in <c>DisplayNames</c>; the feature's name follows it.</summary>
    private const string KeyPrefix = "AccountFeature";

    /// <summary>Projects one feature into its outward view.</summary>
    /// <param name="feature">A single member of <see cref="AccountFeature"/>.</param>
    /// <param name="displayNames">The localized display names, in the caller's language.</param>
    /// <returns>The feature with its display name.</returns>
    public static GrantableFeatureDto From(AccountFeature feature, IStringLocalizer<DisplayNames> displayNames)
    {
        ArgumentNullException.ThrowIfNull(displayNames);

        return new GrantableFeatureDto(feature, displayNames[KeyPrefix + feature].Value);
    }
}
