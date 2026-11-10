using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.SharedKernel.Tests.Utilities.Tokens;

/// <summary>The invitation token generator and the digest stored beside an outstanding invitation.</summary>
public sealed class InvitationTokenHasherTests
{
    /// <summary>A generated token is base64url text with no padding.</summary>
    [Fact]
    public void A_generated_token_is_base64url_text_with_no_padding()
    {
        var token = InvitationTokenHasher.Generate();

        Assert.NotEmpty(token);
        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    /// <summary>Two generated tokens differ.</summary>
    [Fact]
    public void Two_generated_tokens_differ()
    {
        Assert.NotEqual(InvitationTokenHasher.Generate(), InvitationTokenHasher.Generate());
    }

    /// <summary>Hashing the same token twice yields the same digest.</summary>
    [Fact]
    public void Hashing_the_same_token_twice_yields_the_same_digest()
    {
        var token = InvitationTokenHasher.Generate();

        Assert.Equal(InvitationTokenHasher.Hash(token), InvitationTokenHasher.Hash(token));
    }

    /// <summary>The digest of a known token is the published SHA-256 value, base64 encoded.</summary>
    [Fact]
    public void The_digest_of_a_known_token_is_the_published_sha256_value()
    {
        // SHA-256("abc") is ba7816bf...15ad; this is the same 32 bytes in base64.
        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", InvitationTokenHasher.Hash("abc"));
    }

    /// <summary>Different tokens yield different digests.</summary>
    [Fact]
    public void Different_tokens_yield_different_digests()
    {
        Assert.NotEqual(InvitationTokenHasher.Hash("abc"), InvitationTokenHasher.Hash("abd"));
    }
}
