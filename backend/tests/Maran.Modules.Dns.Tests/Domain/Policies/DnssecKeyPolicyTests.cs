using System.Security.Cryptography;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// The key the panel makes and the arithmetic around it. The key tags are the published ones of RFC 4034 section
/// 5.4 and RFC 6605 section 6.1, not values the code was asked for.
/// </summary>
public sealed class DnssecKeyPolicyTests
{
    /// <summary>The RFC 4034 section 5.4 example key (flags 256, algorithm 5, key id 60485).</summary>
    private const string Rfc4034Key =
        "AQOeiiR0GOMYkDshWoSKz9XzfwJr1AYtsmx3TGkJaNXVbfi/2pHm822aJ5iI9BMzNXxeYCmZDRD99WYwYqUSdjMmmAphXdvxegXd/M5+X7OrzKBaMbCVdFLUUh6DhweJBjEVv5f2wwjM9XzcnOf+EPbtG9DMBmADjFDc2w/rljwvFw==";

    /// <summary>The RFC 6605 section 6.1 key (flags 257, algorithm 13, key id 55648).</summary>
    private const string Rfc6605Key =
        "GojIhhXUN/u4v54ZQqGSnyhWJwaubCvTmeexv7bR6edbkrSqQpF64cYbcB7wNcP+e+MAnLr+Wi9xMWyQLc8NAA==";

    /// <summary>The key tag algorithm gives the published key id for the RFC 4034 example.</summary>
    [Fact]
    public void The_key_tag_of_the_rfc_4034_example_key_is_60485()
    {
        Assert.Equal(60485, DnssecKeyPolicy.KeyTag(256, 5, Convert.FromBase64String(Rfc4034Key)));
    }

    /// <summary>The key tag algorithm gives the published key id for the RFC 6605 ECDSA key.</summary>
    [Fact]
    public void The_key_tag_of_the_rfc_6605_key_is_55648()
    {
        Assert.Equal(55648, DnssecKeyPolicy.KeyTag(257, 13, Convert.FromBase64String(Rfc6605Key)));
    }

    /// <summary>Planted difference: changing the flags or one key byte changes the tag, so the tag depends on all of the RDATA.</summary>
    [Fact]
    public void The_key_tag_depends_on_the_flags_and_the_key()
    {
        var key = Convert.FromBase64String(Rfc6605Key);
        var other = (byte[])key.Clone();
        other[63] ^= 0x01;

        Assert.NotEqual(55648, DnssecKeyPolicy.KeyTag(256, 13, key));
        Assert.NotEqual(55648, DnssecKeyPolicy.KeyTag(257, 13, other));
    }

    /// <summary>The DNSKEY RDATA is flags, protocol 3, algorithm, key.</summary>
    [Fact]
    public void The_dnskey_rdata_is_flags_protocol_algorithm_and_key()
    {
        var rdata = DnssecKeyPolicy.DnskeyRdata(257, 13, new byte[] { 0xAA, 0xBB });

        Assert.Equal(new byte[] { 0x01, 0x01, 0x03, 0x0D, 0xAA, 0xBB }, rdata);
    }

    /// <summary>A generated key is a P-256 combined signing key with the lengths the contract states and its own key tag.</summary>
    [Fact]
    public void A_generated_key_is_a_combined_p256_key_with_the_contract_lengths_and_its_own_tag()
    {
        var key = DnssecKeyPolicy.Generate();

        var publicKey = Convert.FromBase64String(key.PublicKey);
        var privateKey = Convert.FromBase64String(key.PrivateKey.Reveal());
        Assert.Equal(DnssecKeyPolicy.PublicKeyLength, publicKey.Length);
        Assert.Equal(DnssecKeyPolicy.PrivateKeyLength, privateKey.Length);
        Assert.Equal(257, key.Flags);
        Assert.Equal(13, key.Algorithm);
        Assert.Equal(DnssecKeyPolicy.KeyTag(257, 13, publicKey), key.KeyTag);
    }

    /// <summary>The halves of a generated key belong together: the scalar signs what the public key verifies.</summary>
    [Fact]
    public void The_two_halves_of_a_generated_key_belong_together()
    {
        var key = DnssecKeyPolicy.Generate();
        var publicKey = Convert.FromBase64String(key.PublicKey);
        var message = "signed by the scalar"u8.ToArray();

        using var signer = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Convert.FromBase64String(key.PrivateKey.Reveal()),
            Q = new ECPoint { X = publicKey[..32], Y = publicKey[32..] },
        });
        var signature = signer.SignData(message, HashAlgorithmName.SHA256);

        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKey[..32], Y = publicKey[32..] },
        });
        Assert.True(verifier.VerifyData(message, signature, HashAlgorithmName.SHA256));
        Assert.False(verifier.VerifyData("another message"u8.ToArray(), signature, HashAlgorithmName.SHA256));
    }

    /// <summary>Two generated keys are different keys.</summary>
    [Fact]
    public void Two_generated_keys_differ()
    {
        var first = DnssecKeyPolicy.Generate();
        var second = DnssecKeyPolicy.Generate();

        Assert.NotEqual(first.PublicKey, second.PublicKey);
        Assert.NotEqual(first.PrivateKey.Reveal(), second.PrivateKey.Reveal());
    }

    /// <summary>A number shorter than 32 bytes is padded on the left, so the public key is always 64 bytes.</summary>
    [Fact]
    public void A_short_number_is_padded_on_the_left_to_thirty_two_bytes()
    {
        var key = DnssecKeyPolicy.FromParts(new byte[] { 0x07 }, new byte[31], new byte[] { 0x09 });

        var publicKey = Convert.FromBase64String(key.PublicKey);
        var privateKey = Convert.FromBase64String(key.PrivateKey.Reveal());
        Assert.Equal(64, publicKey.Length);
        Assert.Equal(0, publicKey[0]);
        Assert.Equal(7, publicKey[31]);
        Assert.Equal(32, privateKey.Length);
        Assert.Equal(9, privateKey[31]);
    }

    /// <summary>A number longer than 32 bytes is refused rather than truncated.</summary>
    [Fact]
    public void A_number_longer_than_thirty_two_bytes_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return DnssecKeyPolicy.FromParts(new byte[33], new byte[32], new byte[32]);
        });
    }

    /// <summary>The generated key's text form masks the private scalar.</summary>
    [Fact]
    public void The_generated_keys_text_form_does_not_print_the_private_scalar()
    {
        var key = DnssecKeyPolicy.Generate();

        Assert.DoesNotContain(key.PrivateKey.Reveal(), key.ToString(), StringComparison.Ordinal);
        Assert.Contains(key.PublicKey, key.ToString(), StringComparison.Ordinal);
    }
}
