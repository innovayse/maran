namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// The four situations a login can really be in, read off the pair the row stores:
/// <see cref="UserState"/> and the independent "this login's account is suspended" fact.
/// </summary>
/// <remarks>
/// <para>
/// The row keeps two fields because a suspension has to be remembered by an unclaimed invitation
/// without consuming it, and that is a stored shape (a column per field), not a rule. Their six
/// combinations include two that no mutator produces; this type names only the four that exist, so
/// a caller asking "may I invite this login?" or "is it usable?" answers one question instead of
/// re-deriving the combination from two fields.
/// </para>
/// <para>
/// Read through <see cref="Entities.User.Lifecycle"/>. It is derived, never stored and never set.
/// </para>
/// </remarks>
public enum UserLifecycle
{
    /// <summary>A claimed login that may sign in.</summary>
    Active,

    /// <summary>An invitation not yet accepted, on an account that is not suspended.</summary>
    Invited,

    /// <summary>
    /// An invitation not yet accepted, on an account that is suspended. Accepting it would still
    /// leave the login unusable, so no new invitation is offered for it.
    /// </summary>
    InvitedAccountSuspended,

    /// <summary>A claimed login whose hosting account is suspended; it keeps its password.</summary>
    Suspended,
}
