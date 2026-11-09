using System.Security.Cryptography;
using Maran.Packaging.Interfaces;
using Maran.Packaging.Resources;

namespace Maran.Packaging.Services;

/// <summary>
/// Verifies the module-signing key's signature over a package manifest: ECDSA on curve P-384 with
/// SHA-384, the signature DER encoded (RFC 3279 <c>ECDSA-Sig-Value</c>), which is exactly what
/// <c>openssl dgst -sha384 -sign</c> writes, so the shell builder and this verifier cannot drift apart.
/// </summary>
/// <remarks>
/// The public key is supplied by the caller and kept as its encoded bytes; each verification imports it
/// afresh, so one instance serves many threads without sharing a key object. It is a third key, never
/// the licence key (Ed25519) and never the release key (P-384, <c>installer/keys/release-signing.pub</c>):
/// a key trusted for one role must not sign in another (rules/security.md item 9), and nothing here
/// carries a default key.
/// </remarks>
public sealed class IpmSignatureVerifier : IIpmSignatureVerifier
{
    /// <summary>The longest DER signature that can be valid for P-384 (two 48-byte integers and their framing is 104 bytes).</summary>
    private const int MaxSignatureBytes = 104;

    /// <summary>The key as SubjectPublicKeyInfo.</summary>
    private readonly byte[] _publicKey;

    /// <summary>Wraps an already validated key.</summary>
    /// <param name="publicKey">The key as SubjectPublicKeyInfo.</param>
    private IpmSignatureVerifier(byte[] publicKey)
    {
        _publicKey = publicKey;
    }

    /// <summary>Builds a verifier from a public key the caller holds.</summary>
    /// <param name="publicKey">A key on curve P-384; only its public half is read.</param>
    /// <returns>The verifier, or <c>IpmPublicKeyInvalid</c> for a key on another curve.</returns>
    public static Result<IpmSignatureVerifier> FromKey(ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        try
        {
            if (!IpmP384Key.Is(publicKey))
            {
                return Invalid();
            }

            return Result<IpmSignatureVerifier>.Ok(new IpmSignatureVerifier(publicKey.ExportSubjectPublicKeyInfo()));
        }
        catch (CryptographicException)
        {
            return Invalid();
        }
    }

    /// <summary>Builds a verifier from a PEM-encoded public key, the form <c>installer/keys/*.pub</c> uses.</summary>
    /// <param name="pem">A <c>PUBLIC KEY</c> PEM block holding a P-384 key.</param>
    /// <returns>The verifier, or <c>IpmPublicKeyInvalid</c> for anything else.</returns>
    public static Result<IpmSignatureVerifier> FromPem(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return FromKey(key);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return Invalid();
        }
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signature.IsEmpty || signature.Length > MaxSignatureBytes)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(_publicKey, out _);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The refusal for a key that cannot be used.</summary>
    /// <returns>The failed result.</returns>
    private static Result<IpmSignatureVerifier> Invalid()
    {
        return Result<IpmSignatureVerifier>.Fail(
            Error.Of(nameof(ErrorMessages.IpmPublicKeyInvalid), ErrorType.Validation));
    }
}
