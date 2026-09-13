using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.SharedKernel.Tests.Utilities.Tokens;

/// <summary>The reset token generator and the digest stored beside a reset request.</summary>
public sealed class PasswordResetTokenHasherTests
{
    /// <summary>A generated token is base64url text with no padding.</summary>
    [Fact]
    public void A_generated_token_is_base64url_text_with_no_padding()
    {
        var token = PasswordResetTokenHasher.Generate();

        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    /// <summary>
    /// <see cref="PasswordResetTokenHasher.Generate"/> now encodes with
    /// <see cref="System.Buffers.Text.Base64Url"/>; this proves it still matches the hand-written
    /// encoder it replaced (<c>Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_')</c>)
    /// byte for byte, since the plaintext token is a one-time secret this repository has no way to
    /// pin as a golden value.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFF })]
    [InlineData(new byte[] { 0xFB, 0xFF, 0xFE, 0x01, 0x02 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 })]
    public void Base64url_encoding_matches_the_hand_written_encoder_it_replaced(byte[] value)
    {
        var expected = Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expected, System.Buffers.Text.Base64Url.EncodeToString(value));
    }

    /// <summary>Hashing the same token twice yields the same digest.</summary>
    [Fact]
    public void Hashing_the_same_token_twice_yields_the_same_digest()
    {
        var token = PasswordResetTokenHasher.Generate();

        Assert.Equal(PasswordResetTokenHasher.Hash(token), PasswordResetTokenHasher.Hash(token));
    }
}
