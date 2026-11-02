namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// The previous release's three-valued login state, kept only as the type of the legacy
/// <c>Users."State"</c> column. <b>Not read by this release</b>: the lifecycle is
/// <see cref="UserLifecycle"/>. Deleted with the column in the contract release.
/// </summary>
public enum UserState
{
    /// <summary>Usable.</summary>
    Active,

    /// <summary>Created for a hosting account, waiting for its owner to set a password. Cannot sign in.</summary>
    Invited,

    /// <summary>Its hosting account is suspended, so it cannot sign in until the account is resumed.</summary>
    Suspended,
}
