using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="PanelLink"/>: the one place a link is joined from the panel's
/// configured origin and a root-relative path.
/// </summary>
public sealed class PanelLinkTests
{
    /// <summary>A configured origin is prefixed exactly once, whether or not it ends in a slash.</summary>
    [Theory]
    [InlineData("https://203.0.113.7:8443")]
    [InlineData("https://203.0.113.7:8443/")]
    public void A_configured_origin_is_prefixed_exactly_once(string origin)
    {
        Assert.Equal("https://203.0.113.7:8443/reset-password?token=t", PanelLink.Compose(origin, "/reset-password?token=t"));
    }

    /// <summary>With no origin the path is returned unchanged.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_no_origin_the_path_is_returned_unchanged(string? origin)
    {
        Assert.False(PanelLink.IsConfigured(origin));
        Assert.Equal("/reset-password?token=t", PanelLink.Compose(origin, "/reset-password?token=t"));
    }

    /// <summary>A configured origin is reported as configured.</summary>
    [Fact]
    public void A_configured_origin_is_reported_as_configured()
    {
        Assert.True(PanelLink.IsConfigured("https://panel.example.com"));
    }
}
