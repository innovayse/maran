namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// Whether a login may act for one hosting account right now. Stored as its name in
/// <c>AccountMemberships."State"</c> and read through <see cref="Entities.AccountMembership.State"/>.
/// </summary>
/// <remarks>
/// The state belongs to the MEMBERSHIP and not to the login, because a login can hold several
/// accounts: suspending one of them must close that account's door and leave the others open. It
/// is a set of named situations rather than a flag so that a third one (an account under review,
/// say) is a new value with a new meaning instead of a second boolean beside the first.
/// </remarks>
public enum MembershipState
{
    /// <summary>The login may act for the account, within its role and grants.</summary>
    Active,

    /// <summary>
    /// The account is suspended. The login keeps the membership, and with it everything it owns, but
    /// cannot sign in to, refresh a session for, switch to or use a feature of this account until
    /// the account is resumed.
    /// </summary>
    Suspended,
}
