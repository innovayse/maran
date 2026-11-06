using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.SharedKernel.Tests.Utilities.Tokens;

/// <summary>The API key minter, its shape check, its digest and the constant-time comparison.</summary>
public sealed class ApiKeyHasherTests
{
    /// <summary>A minted key has the fixed shape: marker, 8 hex prefix, separator, 43 base64url characters.</summary>
    [Fact]
    public void A_minted_key_has_the_documented_shape()
    {
        var (plaintext, prefix, _) = ApiKeyHasher.Mint();

        Assert.Equal(ApiKeyHasher.KeyLength, plaintext.Length);
        Assert.Equal(55, plaintext.Length);
        Assert.StartsWith("mk_" + prefix + "_", plaintext, StringComparison.Ordinal);
        Assert.Equal(8, prefix.Length);
        Assert.Matches("^[0-9a-f]{8}$", prefix);
        Assert.Matches("^mk_[0-9a-f]{8}_[A-Za-z0-9_-]{43}$", plaintext);
    }

    /// <summary>The digest returned by minting is the digest of the plaintext.</summary>
    [Fact]
    public void The_minted_digest_is_the_digest_of_the_plaintext()
    {
        var (plaintext, _, digest) = ApiKeyHasher.Mint();

        Assert.Equal(ApiKeyHasher.Digest(plaintext), digest);
    }

    /// <summary>Two mints never share a key, a prefix or a digest.</summary>
    [Fact]
    public void Two_minted_keys_differ()
    {
        var first = ApiKeyHasher.Mint();
        var second = ApiKeyHasher.Mint();

        Assert.NotEqual(first.Plaintext, second.Plaintext);
        Assert.NotEqual(first.Digest, second.Digest);
    }

    /// <summary>A minted key is accepted by the shape check and yields its own prefix (positive control).</summary>
    [Fact]
    public void A_minted_key_parses_and_yields_its_prefix()
    {
        var (plaintext, prefix, _) = ApiKeyHasher.Mint();

        Assert.True(ApiKeyHasher.TryParse(plaintext, out var parsed));
        Assert.Equal(prefix, parsed);
    }

    /// <summary>The digest is deterministic, base64 SHA-256 text, and differs for a different key.</summary>
    [Fact]
    public void The_digest_is_deterministic_and_differs_per_key()
    {
        var (first, _, _) = ApiKeyHasher.Mint();
        var (second, _, _) = ApiKeyHasher.Mint();

        Assert.Equal(ApiKeyHasher.Digest(first), ApiKeyHasher.Digest(first));
        Assert.NotEqual(ApiKeyHasher.Digest(first), ApiKeyHasher.Digest(second));
        Assert.Equal(44, ApiKeyHasher.Digest(first).Length);
    }

    /// <summary>The digest covers the whole presented string, so a different prefix is a different digest.</summary>
    [Fact]
    public void The_digest_covers_the_prefix_too()
    {
        var (plaintext, prefix, _) = ApiKeyHasher.Mint();
        var altered = plaintext.Replace(prefix, "00000000", StringComparison.Ordinal);

        Assert.NotEqual(ApiKeyHasher.Digest(plaintext), ApiKeyHasher.Digest(altered));
    }

    /// <summary>Equal digests match and the match is symmetric.</summary>
    [Fact]
    public void Equal_digests_match()
    {
        var (_, _, digest) = ApiKeyHasher.Mint();

        Assert.True(ApiKeyHasher.DigestsMatch(digest, digest));
        Assert.True(ApiKeyHasher.DigestsMatch(digest, new string(digest.AsSpan())));
    }

    /// <summary>Digests that differ in the last character, or in length, or are empty do not match (inverse control).</summary>
    [Fact]
    public void Different_digests_do_not_match()
    {
        var (_, _, digest) = ApiKeyHasher.Mint();
        var flipped = digest.Substring(0, digest.Length - 1) + (digest[^1] == 'A' ? 'B' : 'A');

        Assert.False(ApiKeyHasher.DigestsMatch(digest, flipped));
        Assert.False(ApiKeyHasher.DigestsMatch(digest, digest + "A"));
        Assert.False(ApiKeyHasher.DigestsMatch(digest, string.Empty));
    }

    /// <summary>Two keys that share a prefix parse to the same prefix but keep different digests.</summary>
    [Fact]
    public void Keys_sharing_a_prefix_still_have_different_digests()
    {
        const string first = "mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string second = "mk_0a1b2c3d_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        Assert.True(ApiKeyHasher.TryParse(first, out var firstPrefix));
        Assert.True(ApiKeyHasher.TryParse(second, out var secondPrefix));
        Assert.Equal(firstPrefix, secondPrefix);
        Assert.False(ApiKeyHasher.DigestsMatch(ApiKeyHasher.Digest(first), ApiKeyHasher.Digest(second)));
    }

    /// <summary>Malformed header values are refused without throwing and yield an empty prefix.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("mk_")]
    [InlineData("Bearer mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("MK_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk-0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk_0a1b2c3d-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk_0A1B2C3D_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk_0a1b2c3g_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk_0a1b2c3_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    [InlineData("mk_0a1b2c3d_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAé")]
    public void A_malformed_header_value_is_refused_with_an_empty_prefix(string? header)
    {
        Assert.False(ApiKeyHasher.TryParse(header, out var prefix));
        Assert.Equal(string.Empty, prefix);
    }

    /// <summary>A header value of a megabyte is refused by length and never hashed.</summary>
    [Fact]
    public void An_oversized_header_value_is_refused()
    {
        Assert.False(ApiKeyHasher.TryParse(new string('A', 1_000_000), out var prefix));
        Assert.Equal(string.Empty, prefix);
    }
}
