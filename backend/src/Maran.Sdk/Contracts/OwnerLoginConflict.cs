namespace Maran.Sdk.Contracts;

/// <summary>
/// Why a login for a new account's owner could not be made, as the module that creates the account
/// reads it through <see cref="Maran.Sdk.Interfaces.IOwnerLoginDirectory"/>.
/// </summary>
public enum OwnerLoginConflict
{
    /// <summary>No existing login holds the name or the e-mail address: the login can be made.</summary>
    None,

    /// <summary>Another login already has the name the new login would take.</summary>
    UsernameTaken,

    /// <summary>Another login already has the e-mail address the new login would take.</summary>
    EmailTaken,
}
