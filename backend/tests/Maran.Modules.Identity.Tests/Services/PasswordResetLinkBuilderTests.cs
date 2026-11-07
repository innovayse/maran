using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="PasswordResetLinkBuilder"/>: the one place the reset path is
/// written, shared by the reset mail and the break-glass console so the two cannot drift.
/// </summary>
public sealed class PasswordResetLinkBuilderTests
{
    /// <summary>Builds the builder over a configured panel address.</summary>
    /// <param name="panelUrl">The configured public address; empty when none is.</param>
    private static PasswordResetLinkBuilder NewBuilder(string panelUrl)
    {
        return new PasswordResetLinkBuilder(new OptionsWrapper<PanelOptions>(new PanelOptions { PublicUrl = panelUrl }));
    }

    /// <summary>A configured address is prefixed once, whether or not it ends in a slash.</summary>
    [Theory]
    [InlineData("https://panel.example.com")]
    [InlineData("https://panel.example.com/")]
    public void A_configured_address_is_prefixed_once_whether_or_not_it_ends_in_a_slash(string panelUrl)
    {
        var builder = NewBuilder(panelUrl);

        Assert.True(builder.HasPublicAddress);
        Assert.Equal("https://panel.example.com/reset-password?token=abc", builder.Build("abc"));
    }

    /// <summary>With no configured address the link is root-relative and says it has no public address.</summary>
    [Fact]
    public void With_no_configured_address_the_link_is_root_relative()
    {
        var builder = NewBuilder(string.Empty);

        Assert.False(builder.HasPublicAddress);
        Assert.Equal("/reset-password?token=abc", builder.Build("abc"));
    }

    /// <summary>The token is escaped so it cannot end the query or start another parameter.</summary>
    [Fact]
    public void The_token_is_escaped()
    {
        Assert.Equal("/reset-password?token=a%26b%3Dc", PasswordResetLinkBuilder.RelativeFor("a&b=c"));
    }
}
