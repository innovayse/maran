using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maran.Modules.Ssl.Services;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>The JWS envelope and the JWK thumbprint an ACME conversation depends on.</summary>
/// <remarks>
/// Both are things an authority checks and a panel cannot: a wrong thumbprint makes every HTTP-01
/// challenge fail with "the file did not contain the expected content", and a DER-shaped signature
/// makes every signed request fail with "malformed JWS" — neither of which names the actual mistake.
/// </remarks>
public sealed class AcmeSignerTests
{
    /// <summary>The public jwk carries exactly the four members a thumbprint is taken over.</summary>
    [Fact]
    public void The_public_jwk_carries_exactly_the_four_members_a_thumbprint_is_taken_over()
    {
        using var signer = AcmeSigner.CreateNew();

        var members = signer.PublicJwk().Members;

        Assert.Equal(["crv", "kty", "x", "y"], members.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("EC", members["kty"]);
        Assert.Equal("P-256", members["crv"]);
    }

    /// <summary>The thumbprint is the sha256 of the canonical jwk and nothing else.</summary>
    [Fact]
    public void The_thumbprint_is_the_sha256_of_the_canonical_jwk_and_nothing_else()
    {
        using var signer = AcmeSigner.CreateNew();

        var expected = AcmeSigner.Base64Url(
            SHA256.HashData(Encoding.UTF8.GetBytes(signer.PublicJwk().ToCanonicalJson())));

        Assert.Equal(expected, signer.JwkThumbprint());
    }

    /// <summary>A round tripped account key keeps its thumbprint so renewals reuse the same account.</summary>
    [Fact]
    public void A_round_tripped_account_key_keeps_its_thumbprint_so_renewals_reuse_the_same_account()
    {
        using var original = AcmeSigner.CreateNew();
        using var restored = AcmeSigner.FromPem(original.ExportPrivateKeyPem());

        Assert.Equal(original.JwkThumbprint(), restored.JwkThumbprint());
    }

    /// <summary>Base64url output is unpadded and uses the url safe alphabet.</summary>
    [Fact]
    public void Base64url_output_is_unpadded_and_uses_the_url_safe_alphabet()
    {
        var encoded = AcmeSigner.Base64Url([0xFB, 0xFF, 0xFE, 0x01, 0x02]);

        Assert.DoesNotContain('=', encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
    }

    /// <summary>
    /// <see cref="AcmeSigner.Base64Url"/> matches the hand-written encoder it replaced
    /// (<c>Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_')</c>)
    /// byte for byte, across inputs chosen to exercise every padding remainder and both
    /// alphabet substitutions.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFF })]
    [InlineData(new byte[] { 0xFB, 0xFF, 0xFE, 0x01, 0x02 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 })]
    public void Base64url_output_matches_the_hand_written_encoder_it_replaced(byte[] value)
    {
        var expected = Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expected, AcmeSigner.Base64Url(value));
    }

    /// <summary>A signed request names the url and the nonce inside the protected header.</summary>
    [Fact]
    public void A_signed_request_names_the_url_and_the_nonce_inside_the_protected_header()
    {
        using var signer = AcmeSigner.CreateNew();

        var jws = signer.Sign("https://acme.example.com/order", "nonce-1", "{}", "https://acme.example.com/acct/1");

        var header = ProtectedHeader(jws);
        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal("nonce-1", header.GetProperty("nonce").GetString());
        Assert.Equal("https://acme.example.com/order", header.GetProperty("url").GetString());
        Assert.Equal("https://acme.example.com/acct/1", header.GetProperty("kid").GetString());
    }

    /// <summary>A request with no account yet identifies itself by the key instead of a kid.</summary>
    [Fact]
    public void A_request_with_no_account_yet_identifies_itself_by_the_key_instead_of_a_kid()
    {
        using var signer = AcmeSigner.CreateNew();

        var header = ProtectedHeader(signer.Sign("https://acme.example.com/new-acct", "nonce-1", "{}", null));

        Assert.True(header.TryGetProperty("jwk", out _));
        Assert.False(header.TryGetProperty("kid", out _));
    }

    /// <summary>The signature is the sixty four byte fixed width form and not der.</summary>
    [Fact]
    public void The_signature_is_the_sixty_four_byte_fixed_width_form_and_not_der()
    {
        using var signer = AcmeSigner.CreateNew();

        var jws = JsonDocument.Parse(signer.Sign("https://acme.example.com/order", "n", "{}", "acct"));
        var signature = Decode(jws.RootElement.GetProperty("signature").GetString()!);

        // A DER signature is 70-72 bytes and starts with 0x30; ACME authorities reject it.
        Assert.Equal(64, signature.Length);
    }

    /// <summary>Reads and parses the protected header of a flattened JWS.</summary>
    /// <param name="jws">The serialized JWS.</param>
    /// <returns>The decoded header object.</returns>
    private static JsonElement ProtectedHeader(string jws)
    {
        var encoded = JsonDocument.Parse(jws).RootElement.GetProperty("protected").GetString()!;
        return JsonDocument.Parse(Decode(encoded)).RootElement.Clone();
    }

    /// <summary>Decodes unpadded base64url back to bytes.</summary>
    /// <param name="value">The unpadded base64url text.</param>
    /// <returns>The decoded bytes.</returns>
    private static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}
