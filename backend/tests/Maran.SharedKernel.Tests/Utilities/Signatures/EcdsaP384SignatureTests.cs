using System.Security.Cryptography;
using System.Text;
using Maran.SharedKernel.Utilities.Signatures;

namespace Maran.SharedKernel.Tests.Utilities.Signatures;

/// <summary>
/// The one P-384 / SHA-384 verification the release manifest, the module packages and the marketplace
/// catalogue share. Every refusal sits beside the accepted input it is closest to, so a verifier that
/// refuses everything cannot pass.
/// </summary>
public sealed class EcdsaP384SignatureTests
{
    /// <summary>The bytes every test signs.</summary>
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("the exact response bytes");

    /// <summary>Creates a key on NIST P-384.</summary>
    /// <returns>The key.</returns>
    private static ECDsa P384Key()
    {
        return ECDsa.Create(ECCurve.NamedCurves.nistP384);
    }

    /// <summary>Creates a key on NIST P-256, a curve the scheme must refuse.</summary>
    /// <returns>The key.</returns>
    private static ECDsa P256Key()
    {
        return ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    /// <summary>Signs data the way <c>openssl dgst -sha384 -sign</c> does: SHA-384, DER framing.</summary>
    /// <param name="key">The signing key.</param>
    /// <param name="data">The bytes to sign.</param>
    /// <returns>The DER signature.</returns>
    private static byte[] Sign(ECDsa key, byte[] data)
    {
        return key.SignData(data, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
    }

    /// <summary>A signature made by the key over the data is accepted.</summary>
    [Fact]
    public void A_valid_signature_by_the_key_is_accepted()
    {
        using var key = P384Key();

        Assert.True(EcdsaP384Signature.Verify(key.ExportSubjectPublicKeyInfo(), Data, Sign(key, Data)));
    }

    /// <summary>A signature checked against another P-384 key is refused.</summary>
    [Fact]
    public void A_signature_checked_against_another_key_is_refused()
    {
        using var signer = P384Key();
        using var other = P384Key();

        Assert.False(EcdsaP384Signature.Verify(other.ExportSubjectPublicKeyInfo(), Data, Sign(signer, Data)));
    }

    /// <summary>A signature over other bytes is refused.</summary>
    [Fact]
    public void A_signature_over_tampered_data_is_refused()
    {
        using var key = P384Key();
        var signature = Sign(key, Data);
        var tampered = (byte[])Data.Clone();
        tampered[0] ^= 0x01;

        Assert.False(EcdsaP384Signature.Verify(key.ExportSubjectPublicKeyInfo(), tampered, signature));
    }

    /// <summary>A signature longer than any valid P-384 DER signature is refused without being parsed.</summary>
    [Fact]
    public void A_signature_longer_than_104_bytes_is_refused()
    {
        using var key = P384Key();
        var oversized = new byte[EcdsaP384Signature.MaxSignatureBytes + 1];

        Assert.False(EcdsaP384Signature.Verify(key.ExportSubjectPublicKeyInfo(), Data, oversized));
    }

    /// <summary>An empty signature is refused.</summary>
    [Fact]
    public void An_empty_signature_is_refused()
    {
        using var key = P384Key();

        Assert.False(EcdsaP384Signature.Verify(key.ExportSubjectPublicKeyInfo(), Data, ReadOnlySpan<byte>.Empty));
    }

    /// <summary>A key that is not a SubjectPublicKeyInfo is refused without throwing.</summary>
    [Fact]
    public void A_malformed_key_is_refused_without_throwing()
    {
        using var key = P384Key();

        Assert.False(EcdsaP384Signature.Verify(new byte[] { 1, 2, 3 }, Data, Sign(key, Data)));
    }

    /// <summary>A P-256 key is refused even when its own signature is valid (inverse of the accepted case).</summary>
    [Fact]
    public void A_valid_signature_by_a_p256_key_is_refused()
    {
        using var key = P256Key();
        var signature = key.SignData(Data, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);

        Assert.True(key.VerifyData(Data, signature, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence));
        Assert.False(EcdsaP384Signature.Verify(key.ExportSubjectPublicKeyInfo(), Data, signature));
    }

    /// <summary>A key on NIST P-384 is recognised as such.</summary>
    [Fact]
    public void A_p384_key_is_recognised()
    {
        using var key = P384Key();

        Assert.True(EcdsaP384Signature.IsP384(key));
    }

    /// <summary>A key on another curve, of another size, is not recognised.</summary>
    [Fact]
    public void A_p256_key_is_not_recognised()
    {
        using var key = P256Key();

        Assert.False(EcdsaP384Signature.IsP384(key));
    }

    /// <summary>A key on Brainpool P-384, also 384 bits wide, is not recognised: the curve is asked, not the size.</summary>
    [Fact]
    public void A_brainpool_p384_key_is_not_recognised()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.brainpoolP384r1);

        Assert.False(EcdsaP384Signature.IsP384(key));
    }

    /// <summary>A P-384 PEM public key is read to the bytes the key exports.</summary>
    [Fact]
    public void A_p384_pem_is_read_to_its_public_key_bytes()
    {
        using var key = P384Key();

        var bytes = EcdsaP384Signature.TryReadPem(key.ExportSubjectPublicKeyInfoPem());

        Assert.NotNull(bytes);
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), bytes);
    }

    /// <summary>A P-256 PEM public key is not read.</summary>
    [Fact]
    public void A_p256_pem_is_not_read()
    {
        using var key = P256Key();

        Assert.Null(EcdsaP384Signature.TryReadPem(key.ExportSubjectPublicKeyInfoPem()));
    }

    /// <summary>Text that is not a PEM public key is not read and does not throw.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    public void Garbage_is_not_read_as_a_pem_key(string text)
    {
        Assert.Null(EcdsaP384Signature.TryReadPem(text));
    }
}
