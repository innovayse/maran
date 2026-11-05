using Maran.Modules.Idempotency.Domain.Policies;

namespace Maran.Modules.Idempotency.Tests.Domain.Policies;

/// <summary>Which keys, caller keys and hashes may reach a query or a row.</summary>
public sealed class IdempotencyKeyPolicyTests
{
    /// <summary>The characters the key set allows are accepted.</summary>
    [Theory]
    [InlineData("a")]
    [InlineData("Order_42-retry:3.v1")]
    [InlineData("user:11111111-1111-4111-8111-111111111111")]
    public void A_key_made_of_the_allowed_characters_is_accepted(string key)
    {
        Assert.True(IdempotencyKeyPolicy.IsValidKey(key));
    }

    /// <summary>Empty, null, whitespace, control, slash, quote and non-ASCII keys are refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a b")]
    [InlineData("a\r\nb")]
    [InlineData("a/b")]
    [InlineData("a'b")]
    [InlineData("a;b")]
    [InlineData("ա")]
    public void A_key_with_a_forbidden_character_or_no_length_is_refused(string? key)
    {
        Assert.False(IdempotencyKeyPolicy.IsValidKey(key));
    }

    /// <summary>255 characters pass and 256 do not; a caller key stops at 128.</summary>
    [Fact]
    public void The_length_limits_are_exact()
    {
        Assert.True(IdempotencyKeyPolicy.IsValidKey(new string('k', 255)));
        Assert.False(IdempotencyKeyPolicy.IsValidKey(new string('k', 256)));
        Assert.True(IdempotencyKeyPolicy.IsValidCallerKey(new string('c', 128)));
        Assert.False(IdempotencyKeyPolicy.IsValidCallerKey(new string('c', 129)));
    }

    /// <summary>A request hash is exactly 64 lowercase hexadecimal characters.</summary>
    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg", false)]
    [InlineData("", false)]
    public void A_request_hash_is_64_lowercase_hex_characters(string hash, bool expected)
    {
        Assert.Equal(expected, IdempotencyKeyPolicy.IsValidRequestHash(hash));
    }
}
