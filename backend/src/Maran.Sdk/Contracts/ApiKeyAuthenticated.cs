namespace Maran.Sdk.Contracts;

/// <summary>A valid key: the key's id, the scopes it holds and the reseller that owns it, if one does.</summary>
public sealed record ApiKeyAuthenticated : ApiKeyAuthentication
{
    /// <summary>The id of the key that authenticated.</summary>
    public Guid KeyId { get; }

    /// <summary>The scopes the key holds.</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>The reseller that owns the key, or <c>null</c> for a key the administrator owns.</summary>
    public Guid? ResellerId { get; }

    /// <summary>Creates the answer for a valid key.</summary>
    /// <param name="keyId">The id of the key that authenticated.</param>
    /// <param name="scopes">The scopes the key holds.</param>
    /// <param name="resellerId">The reseller that owns the key, or <c>null</c> for the administrator's.</param>
    public ApiKeyAuthenticated(Guid keyId, IReadOnlyList<string> scopes, Guid? resellerId = null)
    {
        KeyId = keyId;
        Scopes = scopes;
        ResellerId = resellerId;
    }
}
