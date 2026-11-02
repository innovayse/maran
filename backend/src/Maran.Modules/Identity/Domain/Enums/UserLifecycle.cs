namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// The four situations a login can really be in. Stored as one value in <c>Users."Lifecycle"</c>
/// and read through <see cref="Entities.User.Lifecycle"/>.
/// </summary>
/// <remarks>
/// A suspension has to be remembered by an unclaimed invitation without consuming it, which is why
/// "invited while its account is suspended" is a state of its own rather than a flag beside a
/// three-valued state. The earlier pair (<see cref="UserState"/> and a suspended flag) had six
/// combinations, two of which no mutator produced; this type names only the four that exist.
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
