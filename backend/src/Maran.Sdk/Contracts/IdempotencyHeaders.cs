namespace Maran.Sdk.Contracts;

/// <summary>The HTTP header names of the idempotency contract.</summary>
public static class IdempotencyHeaders
{
    /// <summary>Request header carrying the caller's idempotency key.</summary>
    public const string Key = "Idempotency-Key";

    /// <summary>Response header, with the value <c>true</c>, on an answer replayed from the store.</summary>
    public const string Replayed = "Idempotent-Replayed";
}
