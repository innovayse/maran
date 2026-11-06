namespace Maran.Sdk.Filters;

/// <summary>
/// Marks an action, already tagged <see cref="IdempotentAttribute"/>, whose answer carries a secret
/// (a generated password, a token): the filter then keeps the key and the request's fingerprint but
/// NEVER the answer, and a replay is refused with <c>IdempotencyResponseNotStored</c> (409) instead of
/// handing the secret out a second time from a table.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IdempotencyNoStoreAttribute : Attribute
{
}
