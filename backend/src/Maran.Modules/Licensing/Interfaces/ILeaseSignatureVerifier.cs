namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// Checks that a lease's signed payload was signed by the lease key pinned in this build. Separate
/// from <see cref="ILicenceSignatureVerifier"/> because the two keys are different keys (decision D3).
/// </summary>
public interface ILeaseSignatureVerifier
{
    /// <summary>Whether this build carries a lease verification key at all.</summary>
    bool HasVerificationKey { get; }

    /// <summary>Checks one signature. Never throws: any failure is <see langword="false"/>.</summary>
    /// <param name="payloadBytes">The exact bytes that were signed.</param>
    /// <param name="signature">The 64-byte Ed25519 signature.</param>
    /// <returns>True only when the signature is valid for the payload under the pinned key.</returns>
    bool Verify(byte[] payloadBytes, byte[] signature);
}
