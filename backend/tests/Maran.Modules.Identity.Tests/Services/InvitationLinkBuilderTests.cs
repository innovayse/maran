using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="InvitationLinkBuilder"/>: the one place the address a token is
/// embedded in is written, shared by the mail and by the administrator's link so the two cannot
/// disagree about where an invitation is accepted.
/// </summary>
public sealed class InvitationLinkBuilderTests
{
    /// <summary>Builds the builder over a configured panel address.</summary>
    /// <param name="panelUrl">The configured public address; empty when none is.</param>
    private static InvitationLinkBuilder NewBuilder(string panelUrl)
    {
        return new InvitationLinkBuilder(new OptionsWrapper<PanelOptions>(new PanelOptions { PanelUrl = panelUrl }));
    }

    /// <summary>A configured address is prefixed once, whether or not it ends in a slash.</summary>
    [Theory]
    [InlineData("https://panel.example.com")]
    [InlineData("https://panel.example.com/")]
    public void A_configured_address_is_prefixed_once_whether_or_not_it_ends_in_a_slash(string panelUrl)
    {
        var link = NewBuilder(panelUrl).Build("abc");

        Assert.Equal("https://panel.example.com/accept-invitation?token=abc", link);
    }

    /// <summary>With no configured address the link is root-relative.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void With_no_configured_address_the_link_is_root_relative(string panelUrl)
    {
        Assert.Equal("/accept-invitation?token=abc", NewBuilder(panelUrl).Build("abc"));
    }

    /// <summary>The token is escaped so it cannot add a parameter or end the query.</summary>
    [Fact]
    public void The_token_is_escaped_so_it_cannot_add_a_parameter_or_end_the_query()
    {
        var relative = InvitationLinkBuilder.RelativeFor("a&b=c#d e");

        Assert.Equal("/accept-invitation?token=a%26b%3Dc%23d%20e", relative);
    }

    /// <summary>The mail and the link agree on the address, because the mail is built from the same path.</summary>
    [Fact]
    public void The_mail_and_the_link_agree_on_the_address_because_the_mail_is_built_from_the_same_path()
    {
        // Pinned through the composer, which is where a second spelling of the path would reappear.
        var composer = new InvitationMailComposer(
            new Maran.Modules.Identity.Tests.TestSupport.StubEmailTemplates(),
            new OptionsWrapper<PanelOptions>(new PanelOptions { PanelUrl = "https://panel.example.com" }));

        var mail = composer.Compose("owner@example.com", "tok en");

        Assert.Contains(NewBuilder("https://panel.example.com").Build("tok en"), mail.Body, StringComparison.Ordinal);
    }
}
