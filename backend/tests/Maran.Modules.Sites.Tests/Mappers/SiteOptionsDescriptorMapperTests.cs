using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>
/// Behavioral contract of the options half of <see cref="SiteDescriptorMapper"/>: every re-render of a
/// vhost carries the site's stored options, because a render without them strips a site of its
/// redirects, credentials and IP rules.
/// </summary>
public sealed class SiteOptionsDescriptorMapperTests
{
    /// <summary>A site with no options is described with none.</summary>
    [Fact]
    public void A_site_with_no_options_is_described_with_none()
    {
        var descriptor = SiteDescriptorMapper.From(SitesTestContext.PhpSite(Guid.NewGuid(), "example.com"));

        Assert.Equal(CanonicalHostKind.None, descriptor.Options.CanonicalHost);
        Assert.Null(descriptor.Options.ClientMaxBodySizeMib);
        Assert.Null(descriptor.Options.StaticCacheDays);
        Assert.Empty(descriptor.Options.Redirects);
        Assert.Empty(descriptor.Options.BasicAuthUsers);
        Assert.Null(descriptor.Options.IpAccess);
        Assert.Null(descriptor.Options.RequestRate);
    }

    /// <summary>Every stored group is carried into the descriptor.</summary>
    [Fact]
    public void Every_stored_group_is_carried_into_the_descriptor()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Full());

        var options = SiteDescriptorMapper.From(site).Options;

        Assert.Equal(CanonicalHostKind.Www, options.CanonicalHost);
        Assert.Equal(64u, options.ClientMaxBodySizeMib);
        Assert.Equal(30u, options.StaticCacheDays);
        var redirect = Assert.Single(options.Redirects);
        Assert.Equal("/old", redirect.Path);
        Assert.Equal(RedirectMatchKind.Exact, redirect.Match);
        Assert.Equal("/new", redirect.Target);
        Assert.Equal(301u, redirect.StatusCode);
        var user = Assert.Single(options.BasicAuthUsers);
        Assert.Equal("alice", user.Name);
        Assert.Equal(SiteOptionsSamples.StoredHash, user.PasswordHash);
        Assert.Equal(IpAccessKind.AllowList, options.IpAccess!.Mode);
        Assert.Equal(["203.0.113.0/24"], options.IpAccess.Cidrs);
        Assert.Equal(new RequestRateDescriptor(10, 20), options.RequestRate);
    }

    /// <summary>A deny list is described as a deny list.</summary>
    [Fact]
    public void A_deny_list_is_described_as_a_deny_list()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Empty() with { IpAccessMode = IpAccessMode.DenyList, IpRules = ["198.51.100.7/32"] });

        Assert.Equal(IpAccessKind.DenyList, SiteDescriptorMapper.From(site).Options.IpAccess!.Mode);
    }

    /// <summary>Redirects and users are described in the order they were stated in.</summary>
    [Fact]
    public void Redirects_and_users_are_described_in_the_order_they_were_stated_in()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Empty() with
        {
            Redirects =
            [
                new SiteRedirectValue("/z", RedirectMatch.Exact, "/1", RedirectStatus.Permanent),
                new SiteRedirectValue("/a/", RedirectMatch.Prefix, "/2/", RedirectStatus.Temporary),
            ],
            BasicAuthUsers =
            [
                new SiteBasicAuthUserValue("zed", SiteOptionsSamples.StoredHash),
                new SiteBasicAuthUserValue("amy", SiteOptionsSamples.StoredHash),
            ],
        });

        var options = SiteDescriptorMapper.From(site).Options;

        Assert.Equal(["/z", "/a/"], options.Redirects.Select(rule =>
        {
            return rule.Path;
        }));
        Assert.Equal(["zed", "amy"], options.BasicAuthUsers.Select(user =>
        {
            return user.Name;
        }));
    }
}
