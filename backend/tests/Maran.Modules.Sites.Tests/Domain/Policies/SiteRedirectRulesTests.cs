using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="SiteRedirectRules"/>: what a redirect path and target may look like.</summary>
public sealed class SiteRedirectRulesTests
{
    /// <summary>Plain rooted paths are accepted.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/old")]
    [InlineData("/blog/")]
    [InlineData("/a/b-c_d.e~f/g")]
    public void Plain_rooted_paths_are_accepted(string path)
    {
        Assert.True(SiteRedirectRules.IsValidPath(path));
    }

    /// <summary>Every character that could end or extend a directive is refused in a path.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("old")]
    [InlineData("/a b")]
    [InlineData("/a;b")]
    [InlineData("/a{b")]
    [InlineData("/a}b")]
    [InlineData("/a\"b")]
    [InlineData("/a'b")]
    [InlineData("/a$b")]
    [InlineData("/a\\b")]
    [InlineData("/a\nb")]
    [InlineData("/a\rb")]
    [InlineData("/a\tb")]
    [InlineData("/a?b=c")]
    [InlineData("/a%2Fb")]
    public void Every_character_that_could_end_or_extend_a_directive_is_refused_in_a_path(string path)
    {
        Assert.False(SiteRedirectRules.IsValidPath(path));
    }

    /// <summary>Empty and dot segments are refused, because the web server normalises them.</summary>
    [Theory]
    [InlineData("//a")]
    [InlineData("/a//b")]
    [InlineData("/a/./b")]
    [InlineData("/a/../b")]
    [InlineData("/..")]
    public void Empty_and_dot_segments_are_refused_because_the_web_server_normalises_them(string path)
    {
        Assert.False(SiteRedirectRules.IsValidPath(path));
    }

    /// <summary>A rule can never shadow the certificate challenge path.</summary>
    [Theory]
    [InlineData("/.well-known/acme-challenge/")]
    [InlineData("/.well-known/acme-challenge/token")]
    [InlineData("/.well-known/acme-challenge")]
    public void A_rule_can_never_shadow_the_certificate_challenge_path(string path)
    {
        Assert.False(SiteRedirectRules.IsValidPath(path));
    }

    /// <summary>The challenge check refuses only that path, so the rest of .well-known is still reachable.</summary>
    [Fact]
    public void The_challenge_check_refuses_only_that_path()
    {
        // The inverse control: a refusal of everything under /.well-known would also pass the test above.
        Assert.True(SiteRedirectRules.IsValidPath("/.well-known/security.txt"));
    }

    /// <summary>A path longer than the bound is refused.</summary>
    [Fact]
    public void A_path_longer_than_the_bound_is_refused()
    {
        Assert.True(SiteRedirectRules.IsValidPath("/" + new string('a', SiteOptionsLimits.MaximumRedirectPathLength - 1)));
        Assert.False(SiteRedirectRules.IsValidPath("/" + new string('a', SiteOptionsLimits.MaximumRedirectPathLength)));
    }

    /// <summary>Absolute http and https URLs with a well-formed host are accepted as targets.</summary>
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/")]
    [InlineData("https://www.example.com/new/path?a=b&c=d#top")]
    [InlineData("https://example.com:8443/x")]
    public void Absolute_http_and_https_urls_with_a_well_formed_host_are_accepted_as_targets(string target)
    {
        Assert.True(SiteRedirectRules.IsValidTarget(target));
    }

    /// <summary>A site path is an accepted target.</summary>
    [Theory]
    [InlineData("/new")]
    [InlineData("/new/")]
    public void A_site_path_is_an_accepted_target(string target)
    {
        Assert.True(SiteRedirectRules.IsValidTarget(target));
    }

    /// <summary>Other schemes and malformed targets are refused, so a rule is never an open redirect to one.</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://example.com/")]
    [InlineData("//evil.example/x")]
    [InlineData("https://")]
    [InlineData("https:///x")]
    [InlineData("https://exa mple.com/")]
    [InlineData("https://example.com/a;b")]
    [InlineData("https://example.com/a\"b")]
    [InlineData("https://example.com/a$b")]
    [InlineData("https://example.com/a\nb")]
    [InlineData("https://user@example.com/")]
    [InlineData("https://-bad-.example/")]
    [InlineData("")]
    [InlineData("new")]
    public void Other_schemes_and_malformed_targets_are_refused(string target)
    {
        Assert.False(SiteRedirectRules.IsValidTarget(target));
    }

    /// <summary>A prefix rule must name a directory.</summary>
    [Fact]
    public void A_prefix_rule_must_name_a_directory()
    {
        Assert.True(SiteRedirectRules.IsValidPrefix("/blog/"));
        Assert.False(SiteRedirectRules.IsValidPrefix("/blog"));
    }
}
