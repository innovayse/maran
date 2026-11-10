namespace Maran.Modules.Licensing.Domain.ValueObjects;

/// <summary>
/// The exact bytes a lease's Ed25519 signature was computed over, together with the detached signature
/// itself, for a lease whose signature has already been checked. Held as one value because a signature
/// means nothing apart from the bytes it covers: the pair is what the licence service issued, and it is
/// what the panel presents back to it.
/// </summary>
/// <remarks>
/// Neither field is ever written to a log or the audit journal (rules/security.md item 8): together they
/// are the credential <see cref="Policies.LeaseBearerToken"/> turns into the marketplace's bearer.
/// </remarks>
/// <param name="PayloadBytes">The exact UTF-8 bytes of the lease payload's JSON text, as signed.</param>
/// <param name="Signature">The detached 64-byte Ed25519 signature over <paramref name="PayloadBytes"/>.</param>
public sealed record SignedLeasePayload(byte[] PayloadBytes, byte[] Signature);
