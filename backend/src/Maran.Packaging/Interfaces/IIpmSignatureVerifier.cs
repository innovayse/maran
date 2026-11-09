namespace Maran.Packaging.Interfaces;

/// <summary>
/// Judges whether a signature is the module-signing key's over some bytes. The key is the only thing a
/// loader substitutes: the open library never embeds one, so it cannot be taught to trust a key by
/// editing it, and a test supplies a throwaway key.
/// </summary>
public interface IIpmSignatureVerifier
{
    /// <summary>Checks a signature over data.</summary>
    /// <param name="data">The signed bytes: the stored bytes of the package manifest.</param>
    /// <param name="signature">The DER-encoded ECDSA signature from the package.</param>
    /// <returns>True only when the signature is valid for <paramref name="data"/> under the supplied key; never throws.</returns>
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature);
}
