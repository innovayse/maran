namespace Maran.Sdk.Contracts;

/// <summary>
/// How the owner of a newly created hosting account gets their first way in. It travels on the wire as a
/// camelCase string (<c>invitation</c>, <c>initialPassword</c>) through the panel-wide
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>, and the SPA is written against
/// those two spellings.
/// </summary>
/// <remarks>
/// A closed two-case choice rather than a boolean beside an optional password: "a password with no mode"
/// and "a mode with no password" are both states the creation request must refuse, and a type that names
/// the mode makes the pairing a rule the validators state instead of a convention.
/// </remarks>
public enum OwnerLoginMode
{
    /// <summary>
    /// The owner is mailed a one-time invitation link and chooses their own password. The default, and the
    /// only mode in which nobody but the owner ever knows the password.
    /// </summary>
    Invitation = 0,

    /// <summary>
    /// The administrator supplies the owner's first password, which the owner must replace at their first
    /// sign-in. For a host that cannot send mail; the administrator knows a credential until it is replaced.
    /// </summary>
    InitialPassword = 1,
}
