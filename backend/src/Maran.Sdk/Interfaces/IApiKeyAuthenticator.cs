using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Decides whether a presented API key is valid. The contract lives in the Sdk and the implementation
/// in the module that stores the keys, so the Host's authentication handler can ask without
/// referencing that module (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// An implementation does the same work for every failure (including a key with an unknown prefix),
/// answers <see cref="ApiKeyDenied"/> for all of them, and never throws for a bad key.
/// </remarks>
public interface IApiKeyAuthenticator
{
    /// <summary>Validates the value of the key header.</summary>
    /// <param name="headerValue">The raw header value, untrusted.</param>
    /// <param name="clientAddress">The caller's address, spelled by <c>ClientAddress</c>.</param>
    /// <param name="now">The current time, for expiry.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The key's id and scopes, or a denial.</returns>
    Task<ApiKeyAuthentication> AuthenticateAsync(
        string headerValue,
        string clientAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
