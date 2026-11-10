using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Decides whether a presented export grant is valid. The contract lives in the Sdk and the implementation in the
/// module that stores the grants, so the Host's authentication handler can ask without referencing that module
/// (the arrangement <see cref="IApiKeyAuthenticator"/> makes for API keys).
/// </summary>
/// <remarks>
/// An implementation does the same work for every failure (a token that matches nothing included), answers
/// <see cref="MigrationGrantDenied"/> for all of them, never throws for a bad token and never logs the token.
/// Authenticating consumes nothing: counting a manifest read or the single read of the artifact is the endpoint's
/// act, because only the endpoint knows which of the two it serves.
/// </remarks>
public interface IMigrationGrantAuthenticator
{
    /// <summary>Validates a presented grant token.</summary>
    /// <param name="token">The token as presented after the <c>Bearer </c> prefix, untrusted.</param>
    /// <param name="clientAddress">The caller's address, spelled by <c>ClientAddress</c>.</param>
    /// <param name="now">The current time, for expiry.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The grant's id and its export, or a denial.</returns>
    Task<MigrationGrantAuthentication> AuthenticateAsync(
        string token,
        string clientAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
