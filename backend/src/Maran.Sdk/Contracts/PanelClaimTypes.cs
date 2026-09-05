namespace Maran.Sdk.Contracts;

/// <summary>
/// Names of the claims the panel's access tokens carry. They live in the Sdk because a module's
/// authorization handler reads them and a module can never reference the Host, and because a
/// marketplace module must be able to reason about the caller without our source.
/// </summary>
/// <remarks>
/// The short registered names (<c>sub</c>, <c>name</c>, <c>sid</c>) are used rather than the long
/// XML-schema URIs .NET maps them to by default: they are what RFC 7519 defines, they keep the
/// token small on every request, and the Host disables inbound claim mapping so what is written
/// here is exactly what is read back.
/// </remarks>
public static class PanelClaimTypes
{
    /// <summary>The panel user's id.</summary>
    public const string UserId = "sub";

    /// <summary>The panel user's login name.</summary>
    public const string Username = "name";

    /// <summary>The user's role: the name of a <c>UserRole</c> member.</summary>
    public const string Role = "role";

    /// <summary>The hosting account a Customer owns. Absent for an administrator, who owns none.</summary>
    public const string AccountId = "account";

    /// <summary>The session this token was issued against, so revoking the session can disown it.</summary>
    public const string SessionId = "sid";

    /// <summary>
    /// Present, with the value <c>true</c>, only while its holder is being steered into two-factor
    /// enrolment and may reach nothing else.
    /// </summary>
    /// <remarks>
    /// The claim's PRESENCE is the restriction: a token issued on a panel that does not force
    /// enrolment carries no such claim at all, so there is no "false" spelling for a reader to parse
    /// wrongly and no way for an absent claim to be mistaken for a permissive one.
    /// </remarks>
    public const string TwoFactorSetupRequired = "tfa_setup";
}
