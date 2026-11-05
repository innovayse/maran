namespace Maran.Sdk.Interfaces;

/// <summary>
/// The keyed fingerprint <see cref="Filters.IdempotencyFilter"/> stores to recognise a repeat of one
/// request. It is keyed on purpose: a request body can carry a password (an account-create body does),
/// and an unsalted digest of it would let anyone who can read the idempotency table guess the secret
/// offline for the whole replay window. Implemented by the Host, which is the only part of the panel
/// that holds the panel's secret; the filter asks the container for it at request time and passes the
/// request through when none is registered, the same degrade rule as <see cref="IIdempotencyStore"/>.
/// </summary>
public interface IIdempotencyFingerprint
{
    /// <summary>Fingerprints the canonical bytes of one request.</summary>
    /// <param name="requestMaterial">The request's method, route, route values, query and body, framed by the caller.</param>
    /// <returns>
    /// Exactly 64 lowercase hexadecimal characters. The same input under the same panel secret always
    /// gives the same answer; under another secret it gives another one.
    /// </returns>
    string Of(byte[] requestMaterial);
}
