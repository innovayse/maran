namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine codes the idempotency filter answers with. The sentences behind them live in the
/// Idempotency module's <c>ErrorMessages</c> table (en, ru, hy), which the panel's error text
/// provider consults like every other module's; the filter lives in the Sdk and cannot reference
/// that module, so it names the codes here.
/// </summary>
public static class IdempotencyErrorCodes
{
    /// <summary>The <c>Idempotency-Key</c> header, or the request it travels with, is not acceptable (400).</summary>
    public const string KeyInvalid = "IdempotencyKeyInvalid";

    /// <summary>
    /// The action requires an <c>Idempotency-Key</c> header and the request carried none (400); see
    /// <see cref="Filters.IdempotentAttribute.Required"/>.
    /// </summary>
    public const string KeyRequired = "IdempotencyKeyRequired";

    /// <summary>The key was already used for a different request (422).</summary>
    public const string KeyReused = "IdempotencyKeyReused";

    /// <summary>A request with this key is still running (409).</summary>
    public const string RequestInProgress = "IdempotencyRequestInProgress";

    /// <summary>The key was used by a request whose answer is deliberately never stored (409).</summary>
    public const string ResponseNotStored = "IdempotencyResponseNotStored";
}
