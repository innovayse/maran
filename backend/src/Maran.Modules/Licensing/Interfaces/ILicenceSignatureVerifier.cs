namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// This module's own seam over Ed25519 signature checking. Kept licensing-specific rather than a
/// general <c>SharedKernel</c> crypto primitive, because what belongs behind it — which embedded
/// public key, which payload shape — is licensing policy, not a reusable cryptographic operation
/// any module would want.
/// </summary>
public interface ILicenceSignatureVerifier
{
    /// <summary>
    /// Checks a detached Ed25519 signature against the embedded licence-verification public key.
    /// </summary>
    /// <param name="payloadBytes">The exact bytes the signature was computed over.</param>
    /// <param name="signature">The detached signature to check.</param>
    /// <returns>
    /// <c>true</c> only when <paramref name="signature"/> is a valid Ed25519 signature of
    /// <paramref name="payloadBytes"/> under the embedded public key. Never throws: a malformed or
    /// wrong-length signature answers <c>false</c>, the same as a mismatched one — see
    /// <see cref="Services.Ed25519LicenceSignatureVerifier"/>'s own remarks for why that is load-bearing
    /// for §229 ("the core never dies").
    /// </returns>
    bool Verify(byte[] payloadBytes, byte[] signature);
}
