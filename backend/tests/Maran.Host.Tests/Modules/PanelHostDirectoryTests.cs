using Maran.Host.Configuration;
using Maran.Host.Modules;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Covers <see cref="PanelHostDirectory"/>: which names it calls the panel's own, and that a missing
/// or unusable configured value reserves nothing and never throws.
/// </summary>
/// <remarks>
/// The panel's name is the one host-specific half of the reserved set, so the two failure shapes
/// both matter: matching too little lets a tenant claim the panel's vhost, matching too much (a
/// sibling subdomain, a malformed value read loosely) refuses legitimate tenant names. The tests
/// pin the exact name and its <c>www.</c> form, and pin that nothing else matches.
/// </remarks>
public sealed class PanelHostDirectoryTests
{
    /// <summary>Builds a directory configured with the given panel name.</summary>
    /// <param name="hostName">The configured value, or null for none.</param>
    /// <returns>The directory under test.</returns>
    private static PanelHostDirectory Build(string? hostName)
    {
        return new PanelHostDirectory(Options.Create(new PanelHostOptions { HostName = hostName }));
    }

    /// <summary>The configured panel name is the panel's own, in any casing.</summary>
    [Theory]
    [InlineData("panel.example.net")]
    [InlineData("PANEL.Example.NET")]
    public void The_configured_panel_name_is_the_panels_own(string candidate)
    {
        Assert.True(Build("panel.example.net").IsPanelHostName(candidate));
    }

    /// <summary>The www form of the configured name is the panel's own, in any casing.</summary>
    [Theory]
    [InlineData("www.panel.example.net")]
    [InlineData("WWW.Panel.Example.Net")]
    public void The_www_form_of_the_panel_name_is_the_panels_own(string candidate)
    {
        Assert.True(Build("panel.example.net").IsPanelHostName(candidate));
    }

    /// <summary>A sibling or child name, or a different domain, is not the panel's own.</summary>
    [Theory]
    [InlineData("shop.panel.example.net")]
    [InlineData("api.panel.example.net")]
    [InlineData("example.net")]
    [InlineData("notpanel.example.net")]
    [InlineData("panel.example.net.evil.test")]
    [InlineData("www.www.panel.example.net")]
    public void A_name_other_than_the_panel_name_and_its_www_form_is_not_the_panels_own(string candidate)
    {
        Assert.False(Build("panel.example.net").IsPanelHostName(candidate));
    }

    /// <summary>A trailing dot or surrounding space in the configured value is normalised away.</summary>
    [Theory]
    [InlineData("panel.example.net.")]
    [InlineData("  panel.example.net  ")]
    [InlineData(" Panel.Example.Net. ")]
    public void A_trailing_dot_or_surrounding_space_in_the_configured_value_is_normalised(string configured)
    {
        var directory = Build(configured);

        Assert.True(directory.IsPanelHostName("panel.example.net"));
        Assert.True(directory.IsPanelHostName("www.panel.example.net"));
    }

    /// <summary>A candidate spelled fully qualified is the same name.</summary>
    [Fact]
    public void A_fully_qualified_candidate_is_the_panels_own()
    {
        Assert.True(Build("panel.example.net").IsPanelHostName("panel.example.net."));
    }

    /// <summary>An absent, blank, placeholder, loopback or malformed value reserves nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("_")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("nodot")]
    [InlineData("has space.example.net")]
    public void An_unusable_configured_value_reserves_nothing_and_does_not_throw(string? configured)
    {
        var directory = Build(configured);

        Assert.False(directory.IsPanelHostName("panel.example.net"));
        Assert.False(directory.IsPanelHostName("www.panel.example.net"));
        Assert.False(directory.IsPanelHostName("localhost"));
        Assert.False(directory.IsPanelHostName("_"));
        Assert.False(directory.IsPanelHostName(string.Empty));
    }

    /// <summary>An empty candidate is never the panel's own, even when a name is configured.</summary>
    [Fact]
    public void An_empty_candidate_is_not_the_panels_own()
    {
        Assert.False(Build("panel.example.net").IsPanelHostName(string.Empty));
    }

    /// <summary>The configured name is handed out normalised by the panel-name window.</summary>
    [Fact]
    public void The_configured_name_is_handed_out_normalised_by_the_panel_name_window()
    {
        Assert.True(Build(" Panel.Example.NET. ").TryGet(out var hostName));
        Assert.Equal("Panel.Example.NET", hostName);
    }

    /// <summary>An unusable configured value hands out no name at all.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("_")]
    [InlineData("localhost")]
    [InlineData("not a host")]
    public void An_unusable_configured_value_hands_out_no_name(string? configured)
    {
        Assert.False(Build(configured).TryGet(out _));
    }

    /// <summary>Builds a directory configured with both the vhost's name and the public address.</summary>
    /// <param name="hostName">The configured vhost name, or null for none.</param>
    /// <param name="publicUrl">The configured public address, or null for none.</param>
    /// <returns>The directory under test.</returns>
    private static PanelHostDirectory BuildBoth(string? hostName, string? publicUrl)
    {
        return new PanelHostDirectory(
            Options.Create(new PanelHostOptions { HostName = hostName, PublicUrl = publicUrl }));
    }

    /// <summary>
    /// The host of the public address is the panel's own although it differs from the vhost's name:
    /// issue #468, where only <c>hostname -f</c> was reserved and the advertised name stayed open to a tenant.
    /// </summary>
    [Theory]
    [InlineData("panel.example.com")]
    [InlineData("PANEL.Example.COM")]
    [InlineData("www.panel.example.com")]
    [InlineData("panel.example.com.")]
    public void The_host_of_the_public_address_is_the_panels_own_beside_a_different_host_name(string candidate)
    {
        var directory = BuildBoth("srv1.provider.net", "https://panel.example.com:8443");

        Assert.True(directory.IsPanelHostName(candidate));
    }

    /// <summary>The vhost's name stays the panel's own when a public address is configured too.</summary>
    [Fact]
    public void The_host_name_stays_the_panels_own_beside_a_public_address()
    {
        var directory = BuildBoth("srv1.provider.net", "https://panel.example.com:8443");

        Assert.True(directory.IsPanelHostName("srv1.provider.net"));
        Assert.True(directory.IsPanelHostName("www.srv1.provider.net"));
    }

    /// <summary>The public address alone reserves its host when no host name is configured.</summary>
    [Fact]
    public void The_public_address_alone_reserves_its_host()
    {
        var directory = BuildBoth(null, "https://panel.example.com");

        Assert.True(directory.IsPanelHostName("panel.example.com"));
    }

    /// <summary>Inverse control: an unrelated domain is accepted when both names are configured.</summary>
    [Theory]
    [InlineData("shop.example.com")]
    [InlineData("example.com")]
    [InlineData("x.panel.example.com")]
    [InlineData("panel.example.com.evil.test")]
    [InlineData("notsrv1.provider.net")]
    public void An_unrelated_domain_is_not_the_panels_own_beside_both_names(string candidate)
    {
        var directory = BuildBoth("srv1.provider.net", "https://panel.example.com:8443");

        Assert.False(directory.IsPanelHostName(candidate));
    }

    /// <summary>A public address with no usable DNS host reserves nothing and never throws.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://panel.example.com")]
    [InlineData("https://user@panel.example.com")]
    [InlineData("https://203.0.113.7")]
    [InlineData("https://[2001:db8::1]:8443")]
    [InlineData("https://localhost:8443")]
    public void An_unusable_public_address_reserves_nothing_and_does_not_throw(string? publicUrl)
    {
        var directory = BuildBoth(null, publicUrl);

        Assert.False(directory.IsPanelHostName("panel.example.com"));
        Assert.False(directory.IsPanelHostName("shop.example.com"));
    }

    /// <summary>The panel-name window still hands out the vhost's name, never the public address's host.</summary>
    [Fact]
    public void The_panel_name_window_hands_out_the_vhost_name_only()
    {
        var directory = BuildBoth("srv1.provider.net", "https://panel.example.com:8443");

        Assert.True(directory.TryGet(out var hostName));
        Assert.Equal("srv1.provider.net", hostName);
    }

    /// <summary>With only a public address the window hands out no name: the panel's certificate is ordered for the vhost's.</summary>
    [Fact]
    public void The_panel_name_window_hands_out_nothing_when_only_a_public_address_is_configured()
    {
        Assert.False(BuildBoth(null, "https://panel.example.com").TryGet(out _));
    }
}
