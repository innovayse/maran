using System.Security.Cryptography;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// A throwaway ECDSA P-384 key a test generates for itself: its public half is written to the file the
/// panel pins as the marketplace metadata key, and its private half signs the fake marketplace's answers.
/// A test never reads a real key, so a key that leaked from here would unlock nothing.
/// </summary>
public sealed class MarketplaceKeyPair : IDisposable
{
    /// <summary>The key, on the curve the panel verifies against.</summary>
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

    /// <summary>The public half as a PEM <c>PUBLIC KEY</c> block, the format the pinned key file holds.</summary>
    public string PublicKeyPem
    {
        get
        {
            return _key.ExportSubjectPublicKeyInfoPem();
        }
    }

    /// <summary>Signs a response body the way the marketplace does: SHA-384, DER, base64.</summary>
    /// <param name="data">The exact bytes of the response body.</param>
    /// <returns>The value of the <c>X-Maran-Signature</c> header.</returns>
    public string Sign(byte[] data)
    {
        return Convert.ToBase64String(
            _key.SignData(data, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key.Dispose();
    }
}
