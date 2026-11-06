using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>Stores API keys minted by the real <see cref="ApiKeyHasher"/> so a test holds both the row and the plaintext that opens it.</summary>
public static class ApiKeySeed
{
    /// <summary>Mints a key, stores its row in <paramref name="context"/> and returns the row with the plaintext.</summary>
    /// <param name="context">The context to store the row in.</param>
    /// <param name="createdAt">The instant the key is issued at.</param>
    /// <param name="name">The key's name.</param>
    /// <param name="scopes">The scopes it carries; the self scope when omitted.</param>
    /// <param name="allowedCidrs">The address ranges it is bound to; none when omitted.</param>
    /// <param name="expiresAt">When it stops being accepted; never when omitted.</param>
    /// <param name="revoked">Whether the key is revoked at <paramref name="createdAt"/>.</param>
    /// <returns>The stored row and the plaintext the digest was taken from.</returns>
    public static async Task<(ApiKey Key, string Plaintext)> AddAsync(
        IdentityDbContext context,
        DateTimeOffset createdAt,
        string name = "CI",
        string[]? scopes = null,
        string[]? allowedCidrs = null,
        DateTimeOffset? expiresAt = null,
        bool revoked = false)
    {
        var (plaintext, prefix, digest) = ApiKeyHasher.Mint();
        var key = ApiKey.Issue(
            Guid.NewGuid(),
            name,
            prefix,
            digest,
            scopes ?? [ApiKeyScopes.SelfRead],
            allowedCidrs,
            Guid.NewGuid(),
            createdAt,
            expiresAt);

        if (revoked)
        {
            key.Revoke(createdAt);
        }

        context.ApiKeys.Add(key);
        await context.SaveChangesAsync();

        return (key, plaintext);
    }
}
