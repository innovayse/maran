using Maran.Modules.Licensing.Interfaces;

namespace Maran.Host.Demo.Adapters;

/// <summary>
/// The demo's <see cref="ILicenceSignatureVerifier"/>: it trusts the demo's own key and nothing else.
/// </summary>
public sealed class DemoLicenceSignatureVerifier : ILicenceSignatureVerifier
{
    /// <summary>The process-lifetime key.</summary>
    private readonly DemoLicenceKey _key;

    /// <summary>Creates the verifier.</summary>
    /// <param name="key">The process-lifetime key.</param>
    public DemoLicenceSignatureVerifier(DemoLicenceKey key)
    {
        _key = key;
    }

    /// <summary>Checks a signature against the demo key.</summary>
    /// <param name="payloadBytes">The exact bytes the signature was computed over.</param>
    /// <param name="signature">The detached signature.</param>
    /// <returns><c>true</c> only for a signature the demo key made; a malformed one answers <c>false</c>.</returns>
    public bool Verify(byte[] payloadBytes, byte[] signature)
    {
        return _key.Verify(payloadBytes, signature);
    }
}
