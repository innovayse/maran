namespace Maran.SharedKernel.Callers;

/// <summary>
/// A machine caller authenticated by an API key: it is not a panel login, and it owns nothing.
/// </summary>
/// <remarks>
/// <para>
/// It is a case of its own, and deliberately NOT a <see cref="PrincipalCaller"/>: a key has no user
/// id, no login name to act as, no role and no operator viewing as it, so deriving from the principal
/// base would hand it members it does not have. It carries the id of the key and the scopes the key
/// was granted, and the id of the reseller that owns it when a reseller does — no user id and no
/// account id.
/// </para>
/// <para>
/// <b>It fails closed everywhere.</b> It is never an <see cref="AdministratorCaller"/>, a
/// <see cref="ResellerCaller"/> or a customer, so every role check refuses it and every tenant query
/// filter shows it no row. The one exception is <see cref="ResellerId"/>: the Accounts module narrows
/// a key a reseller owns to that reseller's own accounts and plans, exactly as it narrows the
/// reseller's login, and every other module still shows the key nothing. What a key may reach is decided by the endpoint it
/// opted into and the scopes it holds (<see cref="Scopes"/>), never by a role. In an audit journal it appears as the
/// actor <see cref="ActorName"/> with no user id, because a key id is not a user id and must not be
/// searched for as one.
/// </para>
/// <para>
/// Record equality compares <see cref="Scopes"/> by reference, as for any collection member; two
/// callers for one key built at different times are not equal. Nothing compares callers by value.
/// </para>
/// </remarks>
public sealed record ApiKeyCaller : Caller
{
    /// <summary>The prefix of <see cref="ActorName"/>, which tells a key apart from a login name.</summary>
    private const string ActorNamePrefix = "api-key:";

    /// <summary>The id of the API key the request was authenticated with.</summary>
    public Guid KeyId { get; }

    /// <summary>The scopes the key was granted, as the closed vocabulary spells them.</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>
    /// The reseller that owns the key, or <c>null</c> for a key the administrator owns. Not a reseller
    /// LOGIN: the key has no user id, and a role check never reads this member.
    /// </summary>
    public Guid? ResellerId { get; }

    /// <summary>
    /// The name an audit journal records this caller under: <c>api-key:</c> followed by the key id in
    /// its 32-digit form (40 characters, within the audit actor column).
    /// </summary>
    public string ActorName
    {
        get { return ActorNamePrefix + KeyId.ToString("N"); }
    }

    /// <summary>Creates an API key caller.</summary>
    /// <param name="keyId">The id of the key the request was authenticated with.</param>
    /// <param name="scopes">The scopes the key was granted.</param>
    /// <param name="resellerId">The reseller that owns the key, or <c>null</c> for the administrator's.</param>
    public ApiKeyCaller(Guid keyId, IReadOnlyList<string> scopes, Guid? resellerId = null)
    {
        KeyId = keyId;
        Scopes = scopes;
        ResellerId = resellerId;
    }
}
