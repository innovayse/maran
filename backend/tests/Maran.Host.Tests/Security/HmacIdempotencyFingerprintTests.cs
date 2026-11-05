using System.Security.Cryptography;
using System.Text;
using Maran.Host.Security;

namespace Maran.Host.Tests.Security;

/// <summary>
/// The keyed fingerprint stored in place of a digest of a request body that may carry a password: it is
/// stable for one panel secret, differs under another, and is never the plain SHA-256 an offline guesser
/// could compute without the secret.
/// </summary>
public sealed class HmacIdempotencyFingerprintTests
{
    /// <summary>A panel secret, base64 of 32 distinct bytes.</summary>
    private static readonly string KeyOne = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    /// <summary>Another panel secret.</summary>
    private static readonly string KeyTwo = Convert.ToBase64String(Enumerable.Range(101, 32).Select(value => (byte)value).ToArray());

    /// <summary>A request body carrying a password, as an account-create body does.</summary>
    private static readonly byte[] Material = Encoding.UTF8.GetBytes("POST\0api/v1/accounts\0{\"password\":\"hunter2\"}");

    /// <summary>The same material under the same secret always fingerprints the same.</summary>
    [Fact]
    public void The_same_input_gives_the_same_output()
    {
        var first = new HmacIdempotencyFingerprint(KeyOne).Of(Material);
        var second = new HmacIdempotencyFingerprint(KeyOne).Of(Material);

        Assert.Equal(first, second);
    }

    /// <summary>The same material under another secret fingerprints differently, so a leaked table cannot be checked without the secret.</summary>
    [Fact]
    public void A_different_key_gives_a_different_output()
    {
        var first = new HmacIdempotencyFingerprint(KeyOne).Of(Material);
        var second = new HmacIdempotencyFingerprint(KeyTwo).Of(Material);

        Assert.NotEqual(first, second);
    }

    /// <summary>Different material under one secret fingerprints differently.</summary>
    [Fact]
    public void A_different_input_gives_a_different_output()
    {
        var fingerprint = new HmacIdempotencyFingerprint(KeyOne);

        Assert.NotEqual(fingerprint.Of(Material), fingerprint.Of(Encoding.UTF8.GetBytes("another body")));
    }

    /// <summary>The fingerprint is never the plain SHA-256 of the material, which anyone could compute offline.</summary>
    [Fact]
    public void The_output_is_never_the_plain_sha256_of_the_input()
    {
        var plain = Convert.ToHexString(SHA256.HashData(Material)).ToLowerInvariant();

        Assert.NotEqual(plain, new HmacIdempotencyFingerprint(KeyOne).Of(Material));
    }

    /// <summary>The MAC key is derived, not the panel secret itself: an HMAC under the raw secret is a different value.</summary>
    [Fact]
    public void The_mac_key_is_derived_and_not_the_panel_secret_itself()
    {
        var raw = Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(KeyOne), Material)).ToLowerInvariant();

        Assert.NotEqual(raw, new HmacIdempotencyFingerprint(KeyOne).Of(Material));
    }

    /// <summary>The output is exactly 64 lowercase hexadecimal characters, what the idempotency column holds.</summary>
    [Fact]
    public void The_output_is_sixty_four_lowercase_hex_characters()
    {
        var output = new HmacIdempotencyFingerprint(KeyOne).Of(Material);

        Assert.Equal(64, output.Length);
        Assert.All(output, character => Assert.True(character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    /// <summary>A missing or malformed secret is refused at construction, not at the first request.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 !!")]
    public void A_missing_or_malformed_secret_is_refused(string secret)
    {
        Assert.ThrowsAny<ArgumentException>(() => new HmacIdempotencyFingerprint(secret));
    }
}
