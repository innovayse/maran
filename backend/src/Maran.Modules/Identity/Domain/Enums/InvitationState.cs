namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// Where a hosting account's owner stands in the invitation they were sent, as an administrator
/// needs to read it: derived from the stored login and token rows at the moment of the question,
/// never stored.
/// </summary>
/// <remarks>
/// Derived rather than persisted so the answer cannot go stale: an invitation that was accepted or
/// that has lapsed reads as such the instant it does, which is what stops the panel from going on
/// offering a link that no longer works.
/// </remarks>
public enum InvitationState
{
    /// <summary>The account has no panel login yet — the issuance at creation never completed.</summary>
    None,

    /// <summary>The login is waiting for its first password and holds at least one usable token.</summary>
    Pending,

    /// <summary>The login is waiting for its first password but every token it was given is spent or expired.</summary>
    Expired,

    /// <summary>The owner has set a password: there is no invitation left to hand over.</summary>
    Accepted,
}
