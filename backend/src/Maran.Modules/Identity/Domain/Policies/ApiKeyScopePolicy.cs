using Maran.Modules.Identity.Domain.Entities;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The one rule for which scopes a stored key actually holds, read by the authenticator and by the
/// key's own view so the two can never disagree.
/// </summary>
public static class ApiKeyScopePolicy
{
    /// <summary>
    /// The scopes a key holds: the stored ones this build still knows, plus the implicit
    /// <see cref="ApiKeyScopes.SelfRead"/> every valid key has. A stored scope the running build does
    /// not know (a rollback, a scope removed since), or that its owner may not be granted (an
    /// administrator-grade scope on a reseller's key, or the reverse), grants nothing.
    /// </summary>
    /// <param name="key">The key row.</param>
    /// <returns>The effective scopes, without duplicates, in the vocabulary's order.</returns>
    public static IReadOnlyList<string> EffectiveOf(ApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return ApiKeyScopes.GrantableTo(key.ResellerId)
            .Where(scope =>
            {
                return scope == ApiKeyScopes.SelfRead || key.Scopes.Contains(scope);
            })
            .ToList();
    }
}
