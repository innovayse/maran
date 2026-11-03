using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Mappers;

/// <summary>Builds a <see cref="MembershipOfferDto"/> from the stored offer.</summary>
public static class MembershipOfferMapper
{
    /// <summary>Projects an offer into the view the offered login sees.</summary>
    /// <param name="offer">The stored offer.</param>
    /// <param name="displayNames">The localized display names, in the caller's language.</param>
    /// <returns>The offer as the offered login's page shows it.</returns>
    public static MembershipOfferDto From(AccountMembershipOffer offer, IStringLocalizer<DisplayNames> displayNames)
    {
        ArgumentNullException.ThrowIfNull(offer);

        var features = offer.Grants.Members()
            .Select(feature =>
            {
                return GrantableFeatureMapper.From(feature, displayNames);
            })
            .ToList();

        return new MembershipOfferDto(offer.Id, offer.AccountName, features, offer.ExpiresAt);
    }
}
