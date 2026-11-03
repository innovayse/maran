namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// The two situations a login itself can be in. Stored as one value in <c>Users."Lifecycle"</c>
/// and read through <see cref="Entities.User.Lifecycle"/>.
/// </summary>
/// <remarks>
/// A login is never "suspended": a suspension is a fact about one hosting account, and a login can
/// hold several, so it is stored on the membership
/// (<see cref="Entities.AccountMembership.State"/>, <see cref="MembershipState"/>). This type used to
/// carry two suspended values; they made a suspension of one account block the whole login and made
/// the second account of a person with two unreachable by suspend or resume.
/// </remarks>
public enum UserLifecycle
{
    /// <summary>
    /// A claimed login: it holds a password and may sign in to any account whose membership allows it.
    /// </summary>
    Active,

    /// <summary>An invitation not yet accepted: the login holds no password and cannot sign in.</summary>
    Invited,
}
