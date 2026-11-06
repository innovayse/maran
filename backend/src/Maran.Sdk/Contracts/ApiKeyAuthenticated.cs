namespace Maran.Sdk.Contracts;

/// <summary>A valid key: the key's id and the scopes it holds.</summary>
public sealed record ApiKeyAuthenticated : ApiKeyAuthentication
{
    /// <summary>The id of the key that authenticated.</summary>
    public Guid KeyId { get; }

    /// <summary>The scopes the key holds.</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>Creates the answer for a valid key.</summary>
    /// <param name="keyId">The id of the key that authenticated.</param>
    /// <param name="scopes">The scopes the key holds.</param>
    public ApiKeyAuthenticated(Guid keyId, IReadOnlyList<string> scopes)
    {
        KeyId = keyId;
        Scopes = scopes;
    }
}
