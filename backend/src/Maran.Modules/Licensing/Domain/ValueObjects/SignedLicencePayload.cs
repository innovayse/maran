namespace Maran.Modules.Licensing.Domain.ValueObjects;

/// <summary>
/// The exact bytes a licence's Ed25519 signature was computed over, together with the detached
/// signature itself. Held as one value because a signature verified against the WRONG bytes is
/// meaningless — the two only mean anything as a pair, never separately.
/// </summary>
/// <remarks>
/// Neither field is ever written to the audit journal (rules/security.md item 8): logging either
/// one turns a log line into a usable copy of signature material, which is exactly what
/// <c>docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md</c> §4 forbids.
/// </remarks>
/// <param name="PayloadBytes">The canonical UTF-8 bytes of the licence's data fields, as signed.</param>
/// <param name="Signature">The detached Ed25519 signature over <paramref name="PayloadBytes"/>.</param>
public sealed record SignedLicencePayload(byte[] PayloadBytes, byte[] Signature);
