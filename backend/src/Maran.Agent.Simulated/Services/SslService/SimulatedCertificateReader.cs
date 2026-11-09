using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Maran.Agent.Client.Resources;

namespace Maran.Agent.Simulated.Services.SslService;

/// <summary>
/// Reads certificate material the way the real agent judges it, and keeps only what the simulation
/// needs from it: when the certificate expires.
/// </summary>
/// <remarks>
/// The material is parsed, not trusted: text that is not a certificate, a key that is not a private
/// key, and a key that belongs to another certificate are refused with the three codes the real
/// agent's refusals translate to. Nothing is validated against a chain or a revocation list, because
/// that is how a lookup reaches the network and this project opens none; and neither the certificate
/// nor the key is ever stored, so a secret handed in is gone when the call returns.
/// </remarks>
public static class SimulatedCertificateReader
{
    /// <summary>The code of a certificate that could not be read.</summary>
    public const string CertificateUnreadableCode = nameof(ErrorMessages.AgentCertificateUnreadable);

    /// <summary>The code of a private key that could not be read.</summary>
    public const string PrivateKeyUnreadableCode = nameof(ErrorMessages.AgentPrivateKeyUnreadable);

    /// <summary>The code of a private key that belongs to another certificate.</summary>
    public const string PrivateKeyMismatchCode = nameof(ErrorMessages.AgentPrivateKeyMismatch);

    /// <summary>Reads the expiry of a certificate and proves the key belongs to it.</summary>
    /// <param name="certificatePem">The PEM-encoded certificate, optionally followed by its chain.</param>
    /// <param name="privateKeyPem">The PEM-encoded private key.</param>
    /// <returns>The moment the leaf certificate expires, or the code naming what was wrong.</returns>
    public static Result<DateTimeOffset> ReadExpiry(string certificatePem, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(certificatePem);
        ArgumentNullException.ThrowIfNull(privateKeyPem);

        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPem(certificatePem);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return Result<DateTimeOffset>.Fail(Error.Of(CertificateUnreadableCode, ErrorType.Validation));
        }

        using (certificate)
        {
            if (!IsReadablePrivateKey(privateKeyPem))
            {
                return Result<DateTimeOffset>.Fail(Error.Of(PrivateKeyUnreadableCode, ErrorType.Validation));
            }

            try
            {
                using var paired = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
            }
            catch (Exception exception) when (exception is ArgumentException or CryptographicException)
            {
                // The certificate parsed and the key imported, so what is left to refuse is the pairing.
                return Result<DateTimeOffset>.Fail(Error.Of(PrivateKeyMismatchCode, ErrorType.Validation));
            }

            return Result<DateTimeOffset>.Ok(new DateTimeOffset(certificate.NotAfter).ToUniversalTime());
        }
    }

    /// <summary>Whether <paramref name="privateKeyPem"/> holds a private key of a kind certificates use.</summary>
    /// <param name="privateKeyPem">The PEM-encoded private key.</param>
    /// <returns>True for an RSA or an elliptic-curve private key.</returns>
    private static bool IsReadablePrivateKey(string privateKeyPem)
    {
        return TryImportRsa(privateKeyPem) || TryImportEllipticCurve(privateKeyPem);
    }

    /// <summary>Whether the key text imports as an RSA private key.</summary>
    /// <param name="privateKeyPem">The PEM-encoded private key.</param>
    /// <returns>True when it does.</returns>
    private static bool TryImportRsa(string privateKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Whether the key text imports as an elliptic-curve private key.</summary>
    /// <param name="privateKeyPem">The PEM-encoded private key.</param>
    /// <returns>True when it does.</returns>
    private static bool TryImportEllipticCurve(string privateKeyPem)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPem);

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}
