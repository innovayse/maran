namespace Maran.Sdk.Contracts;

/// <summary>
/// What <see cref="Interfaces.IIdempotencyStore.BeginAsync"/> answers about a request carrying an
/// idempotency key. Exactly one of four cases, each a type of its own so a case carries only what it
/// has: <see cref="IdempotencyStarted"/>, <see cref="IdempotencyReplay"/>, <see cref="IdempotencyMismatch"/>
/// and <see cref="IdempotencyInProgress"/>.
/// </summary>
/// <remarks>
/// The set is closed by construction (the constructor is <c>private protected</c>), so a <c>switch</c>
/// over it is complete and a case a consumer forgot is a compile-time gap rather than a request
/// silently run twice.
/// </remarks>
public abstract record IdempotencyBegin
{
    /// <summary>Seals the hierarchy to this assembly.</summary>
    private protected IdempotencyBegin()
    {
    }
}
