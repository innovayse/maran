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

    /// <summary>
    /// The reseller a Reseller login acts for. Present only on a token whose <see cref="Role"/> is
    /// <c>Reseller</c>, and then required: a reseller token without it, or with a value that is not a
    /// GUID, is refused rather than read as anything else, exactly as a customer token without
    /// <see cref="AccountId"/> is. Absent for an administrator and for a customer.
    /// </summary>
    public const string ResellerId = "reseller";

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

    /// <summary>
    /// Present, with the value <c>true</c>, only while its holder signed in with a first password somebody
    /// else chose and has not replaced it yet: the token reaches only the endpoints that change the
    /// password, and every other one is refused until that is done.
    /// </summary>
    /// <remarks>
    /// Spelled like <see cref="TwoFactorSetupRequired"/> for the same reason: the claim's PRESENCE is the
    /// restriction, so there is no "false" spelling for a reader to parse wrongly.
    /// </remarks>
    public const string PasswordChangeRequired = "pwd_change";

    /// <summary>
    /// The kind of session the token was issued for. Present, with the value <c>ViewAs</c>, only on a
    /// token an operator holds while viewing the panel as another login (issue #317); an own sign-in
    /// carries no such claim, for the same reason <see cref="TwoFactorSetupRequired"/> has no "false".
    /// </summary>
    public const string SessionKind = "sk";

    /// <summary>
    /// The party actually acting, in the shape RFC 8693 §4.1 defines: a JSON object holding that
    /// party's <see cref="UserId"/>, <see cref="Username"/> and <see cref="SessionId"/>. Present only
    /// on a view-as token; the token's own <c>sub</c> is then the login being viewed as.
    /// </summary>
    /// <remarks>
    /// A reader treats the presence of this claim OR of <see cref="SessionKind"/> as "this is a
    /// view-as token": either one alone is enough to restrict, so a token can never be read as an own
    /// sign-in because one of the two was missing.
    /// </remarks>
    public const string Actor = "act";

    /// <summary>
    /// The id of the API key a request was authenticated with. Written only by the API key
    /// authentication handler; a JWT never carries it, and a token that does is refused rather than
    /// read as a key.
    /// </summary>
    public const string ApiKeyId = "key";

    /// <summary>
    /// One scope an API key holds; the claim repeats once per scope. Written only by the API key
    /// authentication handler, alongside <see cref="ApiKeyId"/>.
    /// </summary>
    public const string ApiKeyScope = "key_scope";

    /// <summary>
    /// The id of the reseller that owns the API key a request was authenticated with. Written only by
    /// the API key authentication handler, and only for a key a reseller owns; a key the administrator
    /// owns carries none. A JWT never carries it.
    /// </summary>
    public const string ApiKeyReseller = "key_reseller";
}
