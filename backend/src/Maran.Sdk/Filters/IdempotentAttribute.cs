namespace Maran.Sdk.Filters;

/// <summary>
/// Opts a mutating action into idempotency: a caller that sends an <c>Idempotency-Key</c> header may
/// repeat the request after a timeout and receive the first answer instead of the work happening twice.
/// A request without the header runs exactly as before. See <see cref="IdempotencyFilter"/> for the
/// semantics.
/// </summary>
/// <remarks>
/// Built on <see cref="TypeFilterAttribute"/> so the filter is constructed per request with the store
/// from the container. An action that returns a secret also carries <see cref="IdempotencyNoStoreAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IdempotentAttribute : TypeFilterAttribute
{
    /// <summary>Creates the attribute.</summary>
    public IdempotentAttribute()
        : base(typeof(IdempotencyFilter))
    {
    }
}
