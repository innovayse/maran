using Maran.SharedKernel.Callers;

namespace Maran.Sdk.Filters;

/// <summary>
/// Turns the caller of a request into the stable string an idempotency key is scoped to, so two callers
/// who happen to pick the same key text can neither replay nor block one another.
/// </summary>
internal static class IdempotencyCallerKey
{
    /// <summary>
    /// The key scope of a caller: <c>user:&lt;id&gt;</c> for a signed-in panel login and
    /// <c>key:&lt;id&gt;</c> for an API-key caller.
    /// </summary>
    /// <param name="caller">Who the request runs as.</param>
    /// <returns>
    /// <c>user:&lt;id&gt;</c> for a panel login, <c>key:&lt;id&gt;</c> for an API-key caller (the key id, in
    /// its 32-digit form, never a user id), or null for a caller that has no stable identity (an
    /// anonymous one), whose requests are passed through without idempotency. The two prefixes keep a
    /// key id and a user id from ever colliding in one scope.
    /// </returns>
    public static string? Of(Caller caller)
    {
        if (caller is PrincipalCaller principal)
        {
            return $"user:{principal.UserId:D}";
        }

        if (caller is ApiKeyCaller apiKey)
        {
            return $"key:{apiKey.KeyId:N}";
        }

        return null;
    }
}
