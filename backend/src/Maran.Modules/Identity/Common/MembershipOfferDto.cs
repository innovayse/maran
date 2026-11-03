namespace Maran.Modules.Identity.Common;

/// <summary>
/// An open offer to join a hosting account's team, as the offered login sees it on their own page.
/// </summary>
/// <param name="Id">The offer's id, the only identifier accepting or declining names.</param>
/// <param name="AccountName">The account's name as it was when the offer was made. The owner's address is never shown.</param>
/// <param name="Features">The features the membership will hold if accepted, with their localized names.</param>
/// <param name="ExpiresAt">The instant after which the offer can no longer be answered.</param>
public sealed record MembershipOfferDto(
    Guid Id,
    string AccountName,
    IReadOnlyList<GrantableFeatureDto> Features,
    DateTimeOffset ExpiresAt);
