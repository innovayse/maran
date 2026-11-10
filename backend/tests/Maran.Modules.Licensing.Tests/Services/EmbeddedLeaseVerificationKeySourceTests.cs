using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="EmbeddedLeaseVerificationKeySource"/>: a key is parsed only when it is exactly 64
/// hexadecimal characters, anything else is "no key", and the key a build embeds is never a test key.
/// </summary>
public sealed class EmbeddedLeaseVerificationKeySourceTests
{
    /// <summary>A well-formed key file yields a configured key; surrounding whitespace is tolerated.</summary>
    [Fact]
    public void Sixty_four_hexadecimal_characters_are_a_configured_key()
    {
        var key = EmbeddedLeaseVerificationKeySource.Parse("  " + new string('a', 64) + "\n");

        Assert.True(key.IsConfigured);
        Assert.Equal(32, key.PublicKey.Length);
    }

    /// <summary>Anything else, including a key of the wrong length or with a non-hex digit, is no key.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000000")]
    public void Anything_else_is_no_key(string text)
    {
        Assert.False(EmbeddedLeaseVerificationKeySource.Parse(text).IsConfigured);
    }

    /// <summary>
    /// The key this build embeds is never the test lease key (the committed seed signs fixtures and is public
    /// by being committed). With no key embedded - the repository's state until the issuer supplies one - the
    /// answer is "no key", which is the safe direction.
    /// </summary>
    [Fact]
    public void The_embedded_production_key_is_not_the_test_lease_key()
    {
        var embedded = EmbeddedLeaseVerificationKeySource.Read();

        Assert.False(
            embedded.IsConfigured && embedded.PublicKey.SequenceEqual(TestKeys.TrustedLeaseKey.PublicKey));
    }

    /// <summary>The control for the test above: the test lease key is a configured key that would be caught.</summary>
    [Fact]
    public void The_test_lease_key_is_a_configured_key_so_the_check_above_can_see_it()
    {
        Assert.True(TestKeys.TrustedLeaseKey.IsConfigured);
        Assert.False(LeaseVerificationKey.None.IsConfigured);
    }
}
