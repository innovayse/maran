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
}
