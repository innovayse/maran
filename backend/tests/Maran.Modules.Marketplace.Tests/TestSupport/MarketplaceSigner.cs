using System.Security.Cryptography;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// A throwaway ECDSA P-384 key generated for one test, standing in for the marketplace's signing key. No
/// key is ever committed: every test that needs a signer makes its own and discards it.
/// </summary>
public sealed class MarketplaceSigner : IDisposable
{
    /// <summary>The private key; never leaves this object.</summary>
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

    /// <summary>The public half as a PEM <c>PUBLIC KEY</c> block, the form the pinned key file holds.</summary>
    public string PublicKeyPem
    {
        get
        {
            return _key.ExportSubjectPublicKeyInfoPem();
        }
    }

    /// <summary>Signs bytes the way the service does: SHA-384, DER framing, base64.</summary>
    /// <param name="data">The exact bytes the signature must cover.</param>
    /// <returns>The value of the <c>X-Maran-Signature</c> header.</returns>
    public string Sign(byte[] data)
    {
        return Convert.ToBase64String(_key.SignData(data, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence));
    }

    /// <summary>Releases the key.</summary>
    public void Dispose()
    {
        _key.Dispose();
    }
}
