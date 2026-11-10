using System.Buffers.Text;
using System.Text;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;

namespace Maran.Modules.Licensing.Tests.Domain.Policies;

/// <summary>
/// Covers <see cref="LeaseBearerToken"/>, the one spelling of the lease as a bearer credential: two
/// unpadded base64url parts joined by a dot, the payload first, and nothing else.
/// </summary>
public sealed class LeaseBearerTokenTests
{
    /// <summary>The token is the payload bytes, a dot and the signature bytes, each as unpadded base64url.</summary>
    [Fact]
    public void The_token_is_the_payload_and_the_signature_as_unpadded_base64url_joined_by_a_dot()
    {
        var payload = Encoding.UTF8.GetBytes("{\"kind\":\"lease\"}");
        var signature = new byte[] { 0xFB, 0xFF, 0xFE, 0x01 };

        var token = LeaseBearerToken.Of(new SignedLeasePayload(payload, signature));

        Assert.Equal("eyJraW5kIjoibGVhc2UifQ.-__-AQ", token);
    }

    /// <summary>Both parts decode back to exactly the bytes the service signed, so the service can verify its own signature.</summary>
    [Fact]
    public void Both_parts_decode_to_the_bytes_that_went_in()
    {
        var payload = Encoding.UTF8.GetBytes("{\"kind\":\"lease\",\"licence\":\"lic-001\"}");
        var signature = Enumerable.Range(0, 64).Select(index => { return (byte)index; }).ToArray();

        var token = LeaseBearerToken.Of(new SignedLeasePayload(payload, signature));

        var parts = token.Split(LeaseBearerToken.Separator);
        Assert.Equal(2, parts.Length);
        Assert.Equal(payload, Base64Url.DecodeFromChars(parts[0]));
        Assert.Equal(signature, Base64Url.DecodeFromChars(parts[1]));
    }

    /// <summary>The token never needs escaping in a header: no padding, no plus, no slash, no whitespace.</summary>
    [Fact]
    public void The_token_uses_only_the_url_safe_alphabet()
    {
        var payload = Enumerable.Range(0, 255).Select(index => { return (byte)index; }).ToArray();
        var signature = new byte[] { 0xFF, 0xFF, 0xFF, 0xFE };

        var token = LeaseBearerToken.Of(new SignedLeasePayload(payload, signature));

        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain(' ', token);
    }
}
