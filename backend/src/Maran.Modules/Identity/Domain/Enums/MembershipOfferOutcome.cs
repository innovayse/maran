namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// How an <see cref="Entities.AccountMembershipOffer"/> was resolved. Stored as its name in
/// <c>AccountMembershipOffers."Outcome"</c>; an offer that is still open has no outcome at all.
/// </summary>
public enum MembershipOfferOutcome
{
    /// <summary>The offered login accepted, and the membership was created in the same transaction.</summary>
    Accepted,

    /// <summary>The offered login declined.</summary>
    Declined,

    /// <summary>The account's owner withdrew the offer before it was answered.</summary>
    Cancelled,

    /// <summary>
    /// The offer reached its expiry without an answer and was closed so that a fresh offer to the
    /// same login can be stored. Nothing is granted by it.
    /// </summary>
    Expired,
}
