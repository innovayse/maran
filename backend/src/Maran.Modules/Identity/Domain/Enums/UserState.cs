namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>Whether a panel login may be used, and why not when it may not.</summary>
/// <remarks>
/// <see cref="Active"/> is declared first so it is the enum's zero value: the existing
/// <c>User</c> constructor (used by <c>CompleteSetup</c> for the administrator) never sets
/// <c>State</c> explicitly, so an administrator's login must default to usable rather than to
/// the invited state a hosting account's owner starts in.
/// </remarks>
public enum UserState
{
    /// <summary>Usable.</summary>
    Active,

    /// <summary>Created for a hosting account, waiting for its owner to set a password. Cannot sign in.</summary>
    Invited,

    /// <summary>Its hosting account is suspended, so it cannot sign in until the account is resumed.</summary>
    Suspended,
}
