using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>What the setup token policy refuses: the published sample, and tokens too short or uniform.</summary>
public sealed class SetupTokenPolicyTests
{
    /// <summary>A token shaped like the installer's own (48 hex digits), built from hash output.</summary>
    private static readonly string InstallerShapedToken = Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData("installer-token"u8.ToArray()))[..48];

    /// <summary>The published sample is recognised, in any case and with surrounding whitespace.</summary>
    /// <param name="token">A spelling of the published sample.</param>
    [Theory]
    [InlineData("dev-setup-token")]
    [InlineData("DEV-Setup-Token")]
    [InlineData("  dev-setup-token\n")]
    public void The_published_sample_is_recognised(string token)
    {
        Assert.True(SetupTokenPolicy.IsPublishedSample(token));
    }

    /// <summary>An empty or absent token is neither published nor weak: a finished panel has none.</summary>
    /// <param name="token">An empty or absent token.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_token_is_neither_published_nor_weak(string? token)
    {
        Assert.False(SetupTokenPolicy.IsPublishedSample(token));
        Assert.False(SetupTokenPolicy.IsWeak(token));
    }

    /// <summary>Short and uniform tokens are weak, including the published one.</summary>
    /// <param name="token">A token the floor must refuse.</param>
    [Theory]
    [InlineData("a")]
    [InlineData("   ")]
    [InlineData("dev-setup-token")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("abababababababababababababababababababab")]
    public void A_short_or_uniform_token_is_weak(string token)
    {
        Assert.True(SetupTokenPolicy.IsWeak(token));
    }

    /// <summary>The inverse control: a token of the installer's shape, and a 32-character one, are accepted.</summary>
    [Fact]
    public void A_token_of_the_installers_shape_is_accepted()
    {
        Assert.Equal(48, InstallerShapedToken.Length);
        Assert.False(SetupTokenPolicy.IsWeak(InstallerShapedToken));
        Assert.False(SetupTokenPolicy.IsWeak(InstallerShapedToken[..SetupTokenPolicy.MinimumLength]));
        Assert.False(SetupTokenPolicy.IsPublishedSample(InstallerShapedToken));
    }
}
