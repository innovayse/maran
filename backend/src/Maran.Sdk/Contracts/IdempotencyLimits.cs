namespace Maran.Sdk.Contracts;

/// <summary>The bounds of the idempotency contract, stated once for the filter, its tests and the store.</summary>
public static class IdempotencyLimits
{
    /// <summary>Longest accepted <c>Idempotency-Key</c>, in characters.</summary>
    public const int MaxKeyLength = 255;

    /// <summary>
    /// Largest request body, in bytes, the filter will fingerprint. A larger body is refused with
    /// <see cref="IdempotencyErrorCodes.KeyInvalid"/> rather than run unprotected: a caller who sent a
    /// key asked for a guarantee the filter cannot give for a body it will not read.
    /// </summary>
    public const int MaxRequestBodyBytes = 1024 * 1024;

    /// <summary>Largest serialized response, in bytes, the filter stores; a larger answer is not stored and the key is released.</summary>
    public const int MaxStoredBodyBytes = 256 * 1024;
}
