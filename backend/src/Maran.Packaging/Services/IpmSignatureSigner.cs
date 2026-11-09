using System.Security.Cryptography;
using Maran.Packaging.Resources;

namespace Maran.Packaging.Services;

/// <summary>
/// Signs a package manifest with a key the caller supplies: ECDSA on curve P-384 with SHA-384, DER
/// encoded, the exact counterpart of <see cref="IpmSignatureVerifier"/>. The library never holds,
/// generates or defaults a private key: the module-signing key lives offline and outside this
/// repository, and a test passes a throwaway one.
/// </summary>
public static class IpmSignatureSigner
{
    /// <summary>Signs the stored bytes of <c>ipm.json</c>.</summary>
    /// <param name="manifestBytes">The exact bytes that will be stored as <c>ipm.json</c>.</param>
    /// <param name="privateKey">A P-384 key holding its private half.</param>
    /// <returns>The DER signature to store as <c>ipm.sig</c>, or <c>IpmPublicKeyInvalid</c> when the key is not a usable P-384 signing key.</returns>
    public static Result<byte[]> Sign(ReadOnlySpan<byte> manifestBytes, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        try
        {
            if (!IpmP384Key.Is(privateKey))
            {
                return Invalid();
            }

            var signature = privateKey.SignData(
                manifestBytes,
                HashAlgorithmName.SHA384,
                DSASignatureFormat.Rfc3279DerSequence);

            return Result<byte[]>.Ok(signature);
        }
        catch (CryptographicException)
        {
            return Invalid();
        }
    }

    /// <summary>The refusal for a key that cannot sign.</summary>
    /// <returns>The failed result.</returns>
    private static Result<byte[]> Invalid()
    {
        return Result<byte[]>.Fail(Error.Of(nameof(ErrorMessages.IpmPublicKeyInvalid), ErrorType.Validation));
    }
}
