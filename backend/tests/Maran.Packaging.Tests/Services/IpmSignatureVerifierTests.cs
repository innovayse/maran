using System.Security.Cryptography;
using System.Text;
using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;

namespace Maran.Packaging.Tests.Services;

/// <summary>
/// The signature scheme: ECDSA P-384 with SHA-384, DER encoded. The signer and the verifier are tested
/// against each other, and the verifier against a signature that OpenSSL made, so the two sides of the
/// format cannot drift apart unnoticed.
/// </summary>
public sealed class IpmSignatureVerifierTests : IDisposable
{
    /// <summary>
    /// A P-384 public key generated once with OpenSSL for this fixture. The private half was destroyed
    /// at generation and is not in this repository; the key signs nothing and trusts nothing.
    /// </summary>
    private const string OpenSslPublicKey =
        "-----BEGIN PUBLIC KEY-----\n"
        + "MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAE+arhg9glEex6w5XHulhOXGfFliCjG/34\n"
        + "e7G1uEYmA8Bq2o6JTMKjy+UAZv3rUq/8NY4NZbyRGPy9anP3w8V8bpygcZanzTY7\n"
        + "nozZ80Zx9hcVa6kcaumoMHxPu1zlot7B\n"
        + "-----END PUBLIC KEY-----\n";

    /// <summary>The bytes OpenSSL signed.</summary>
    private const string OpenSslSignedText = "maran ipm openssl cross-check";

    /// <summary>
    /// What <c>openssl dgst -sha384 -sign &lt;key&gt; -out sig.der</c> wrote over
    /// <see cref="OpenSslSignedText"/>, in base64 (OpenSSL 3.0.13), and which
    /// <c>openssl dgst -sha384 -verify</c> accepted when it was made.
    /// </summary>
    private const string OpenSslSignature =
        "MGQCMGmHhrNC4oTXo68GEy9hyBKyq7nyMun3lYX5POh1xMDgVJJk9uknrI1n8c3YBHIargIwMWKlbSAi4hPg1dlCekXPd34m"
        + "l0lJZeCwMsc7s4creUQ3rolErvmGPrIeZC7SUMAM";

    /// <summary>The throwaway key of this run.</summary>
    private readonly ECDsa _key = IpmFixtureBuilder.NewKey();

    /// <summary>Frees the throwaway key.</summary>
    public void Dispose()
    {
        _key.Dispose();
    }

    /// <summary>A signature made by the signer verifies, and is a DER sequence no longer than P-384 allows.</summary>
    [Fact]
    public void A_signature_made_by_the_signer_verifies_and_is_der_encoded()
    {
        var data = Encoding.UTF8.GetBytes("manifest bytes");

        var signature = IpmSignatureSigner.Sign(data, _key);
        var verifier = IpmSignatureVerifier.FromKey(_key);

        Assert.True(signature.IsSuccess, signature.Error?.Code);
        Assert.True(verifier.IsSuccess, verifier.Error?.Code);
        Assert.Equal(0x30, signature.Value[0]);
        Assert.InRange(signature.Value.Length, 8, 104);
        Assert.True(verifier.Value.Verify(data, signature.Value));
    }

    /// <summary>Changed data, a signature by another key, garbage, an empty and an over-long signature are all refused.</summary>
    [Fact]
    public void Changed_data_another_key_and_garbage_are_refused()
    {
        var data = Encoding.UTF8.GetBytes("manifest bytes");
        using var other = IpmFixtureBuilder.NewKey();
        var verifier = IpmSignatureVerifier.FromKey(_key).Value;
        var good = IpmSignatureSigner.Sign(data, _key).Value;
        var byOther = IpmSignatureSigner.Sign(data, other).Value;

        Assert.True(verifier.Verify(data, good));
        Assert.False(verifier.Verify(Encoding.UTF8.GetBytes("manifest byteS"), good));
        Assert.False(verifier.Verify(data, byOther));
        Assert.False(verifier.Verify(data, new byte[96]));
        Assert.False(verifier.Verify(data, ReadOnlySpan<byte>.Empty));
        Assert.False(verifier.Verify(data, new byte[500]));
    }

    /// <summary>A signature OpenSSL made with SHA-384 over fixed bytes verifies, and the same signature over changed bytes does not.</summary>
    [Fact]
    public void A_signature_made_by_openssl_verifies()
    {
        var verifier = IpmSignatureVerifier.FromPem(OpenSslPublicKey);
        var signature = Convert.FromBase64String(OpenSslSignature);

        Assert.True(verifier.IsSuccess, verifier.Error?.Code);
        Assert.True(verifier.Value.Verify(Encoding.ASCII.GetBytes(OpenSslSignedText), signature));
        Assert.False(verifier.Value.Verify(Encoding.ASCII.GetBytes(OpenSslSignedText + "."), signature));
    }

    /// <summary>A key on another curve is refused by both the verifier and the signer, and a P-384 key is accepted by both.</summary>
    [Fact]
    public void A_key_on_another_curve_is_refused_by_the_verifier_and_the_signer()
    {
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var brainpool = ECDsa.Create(ECCurve.NamedCurves.brainpoolP384r1);

        RefusalAssert.Is("IpmPublicKeyInvalid", IpmSignatureVerifier.FromKey(p256).Error);
        RefusalAssert.Is("IpmPublicKeyInvalid", IpmSignatureSigner.Sign([1], p256).Error);
        RefusalAssert.Is("IpmPublicKeyInvalid", IpmSignatureVerifier.FromKey(brainpool).Error);
        Assert.True(IpmSignatureVerifier.FromKey(_key).IsSuccess);
        Assert.True(IpmSignatureSigner.Sign([1], _key).IsSuccess);
    }

    /// <summary>Text that is not a public key PEM is refused rather than thrown.</summary>
    [Fact]
    public void Text_that_is_not_a_public_key_is_refused()
    {
        RefusalAssert.Is("IpmPublicKeyInvalid", IpmSignatureVerifier.FromPem("not a key").Error);
        RefusalAssert.Is("IpmPublicKeyInvalid", IpmSignatureVerifier.FromPem(string.Empty).Error);
        Assert.True(IpmSignatureVerifier.FromPem(_key.ExportSubjectPublicKeyInfoPem()).IsSuccess);
    }
}
