using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maran.Modules.Ssl.Common;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// The cryptographic half of an ACME conversation: holds the account key and produces the signed
/// JWS envelope every ACME request but the directory fetch is wrapped in (RFC 8555 §6.2).
/// </summary>
/// <remarks>
/// This is protocol plumbing, not home-grown cryptography (rules/security.md item 9): the signature
/// is <see cref="ECDsa"/> over the NIST P-256 curve from the platform library, and this type only
/// assembles the bytes the specification says to sign. ES256 is the algorithm every ACME authority
/// is required to accept, and a P-256 key is small enough that generating a fresh one costs nothing.
///
/// The key is a secret. It is never logged, never rendered into an error, and reaches the database
/// only through <c>IEncryptionService</c> — an ACME account key is authority to issue certificates
/// for every domain that account has ever validated.
/// </remarks>
public sealed class AcmeSigner : IDisposable
{
    /// <summary>The JWS algorithm this signer produces, and the only one it can.</summary>
    private const string Algorithm = "ES256";

    /// <summary>Size in bytes of one half of a P-256 signature, so R and S can be padded to fixed width.</summary>
    private const int CoordinateBytes = 32;

    /// <summary>The account key. Owned by this instance and disposed with it.</summary>
    private readonly ECDsa _key;

    /// <summary>Wraps an existing account key.</summary>
    /// <param name="key">The P-256 key to sign with; this instance takes ownership of it.</param>
    private AcmeSigner(ECDsa key)
    {
        _key = key;
    }

    /// <summary>Creates a signer over a brand-new account key.</summary>
    /// <returns>A signer whose key has never been used.</returns>
    public static AcmeSigner CreateNew()
    {
        return new AcmeSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));
    }

    /// <summary>Creates a signer over an existing account key in PEM form.</summary>
    /// <param name="privateKeyPem">The PEM-encoded P-256 private key.</param>
    /// <returns>A signer that will produce the same JWK thumbprint as before.</returns>
    public static AcmeSigner FromPem(string privateKeyPem)
    {
        var key = ECDsa.Create();

        try
        {
            key.ImportFromPem(privateKeyPem);
        }
        catch
        {
            // A stored key that will not import is a real possibility — a truncated column, a wrong
            // encryption key — and the platform object holds unmanaged state. Without this the
            // handle survives to the finalizer queue on every failed order.
            key.Dispose();
            throw;
        }

        return new AcmeSigner(key);
    }

    /// <summary>Base64url-encodes bytes: RFC 4648 §5 without padding, as every ACME field uses.</summary>
    /// <param name="value">The bytes to encode.</param>
    /// <returns>The unpadded base64url text.</returns>
    public static string Base64Url(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Exports the account key so it can be stored, encrypted, and used again.</summary>
    /// <returns>The PEM-encoded private key. A secret: it goes to encrypted storage and nowhere else.</returns>
    public string ExportPrivateKeyPem()
    {
        return _key.ExportECPrivateKeyPem();
    }

    /// <summary>Builds the account key's public JWK, which identifies a brand-new account.</summary>
    /// <returns>The JWK object, with its members in the order a thumbprint requires.</returns>
    /// <remarks>
    /// The member order is not cosmetic. RFC 7638 defines a JWK thumbprint over the canonical
    /// serialization — the required members, lexicographically ordered, with no whitespace — so
    /// <c>crv</c>, <c>kty</c>, <c>x</c>, <c>y</c> is the order the hash is taken over, and the same
    /// object is reused for the protected header so the two can never disagree.
    /// </remarks>
    public JsonObjectValue PublicJwk()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        return new JsonObjectValue(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["crv"] = "P-256",
            ["kty"] = "EC",
            ["x"] = Base64Url(parameters.Q.X!),
            ["y"] = Base64Url(parameters.Q.Y!),
        });
    }

    /// <summary>Computes the RFC 7638 thumbprint of the account key, base64url-encoded.</summary>
    /// <returns>The thumbprint, which is the second half of every HTTP-01 key authorization.</returns>
    public string JwkThumbprint()
    {
        var canonical = PublicJwk().ToCanonicalJson();
        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Signs one ACME request as a flattened JWS (RFC 8555 §6.2).</summary>
    /// <param name="url">The request's target URL, which the specification requires inside the signature.</param>
    /// <param name="nonce">The anti-replay nonce the authority last handed out.</param>
    /// <param name="payload">The request body, or the empty string for a POST-as-GET.</param>
    /// <param name="accountUrl">
    /// The account's URL, sent as <c>kid</c>. Null only for the request that creates the account,
    /// which identifies itself by the key itself instead — an account that does not exist yet has no
    /// URL to name.
    /// </param>
    /// <returns>The JSON body to POST, with content type <c>application/jose+json</c>.</returns>
    public string Sign(string url, string nonce, string payload, string? accountUrl)
    {
        var header = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["alg"] = Algorithm,
            ["nonce"] = nonce,
            ["url"] = url,
        };

        if (accountUrl is null)
        {
            header["jwk"] = PublicJwk().Members;
        }
        else
        {
            header["kid"] = accountUrl;
        }

        var protectedHeader = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header)));
        var encodedPayload = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signingInput = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{protectedHeader}.{encodedPayload}"));

        // IeeeP1363FixedFieldConcatenation, not the DER default: JWS carries R and S as two
        // fixed-width integers, and a DER signature is rejected by every ACME authority.
        var signature = _key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != CoordinateBytes * 2)
        {
            throw new InvalidOperationException("A P-256 JWS signature must be exactly 64 bytes.");
        }

        return JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["protected"] = protectedHeader,
            ["payload"] = encodedPayload,
            ["signature"] = Base64Url(signature),
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key.Dispose();
    }
}
