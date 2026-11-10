using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.V1;
using SiteWebServer = Maran.Agent.Client.Services.SitesService.SiteWebServer;

namespace Maran.Agent.Client.Tests.Services.SitesService;

/// <summary>
/// Wire contract of <see cref="SiteOptionsDescriptor"/> inside <see cref="SiteDescriptor"/>: every
/// option the panel stores reaches the agent's message, and an absent one is the message's zero value.
/// </summary>
public sealed class SiteOptionsDescriptorTests
{
    /// <summary>A descriptor with every group filled in writes every group into the spec's options.</summary>
    [Fact]
    public void A_descriptor_with_every_group_filled_in_writes_every_group_into_the_specs_options()
    {
        var descriptor = new SiteDescriptor(
            ["www.example.com"],
            SiteBackendKind.Php,
            "8.3",
            string.Empty,
            SiteNodeApp.None,
            SiteWebServer.Nginx,
            true,
            new SiteOptionsDescriptor(
                CanonicalHostKind.Apex,
                64,
                30,
                [
                    new RedirectRuleDescriptor("/old", RedirectMatchKind.Exact, "/new", 301),
                    new RedirectRuleDescriptor("/blog/", RedirectMatchKind.Prefix, "https://example.org/blog/", 302),
                ],
                [new BasicAuthUserDescriptor("alice", "$2y$12$hash")],
                new IpAccessDescriptor(IpAccessKind.DenyList, ["198.51.100.0/24"]),
                new RequestRateDescriptor(25, 50)));

        var options = descriptor.ToWire().Options;

        Assert.Equal(CanonicalHost.Apex, options.CanonicalHost);
        Assert.Equal(64u, options.ClientMaxBodySizeMib);
        Assert.Equal(30u, options.StaticCacheDays);
        Assert.Equal(2, options.Redirects.Count);
        Assert.Equal("/old", options.Redirects[0].Path);
        Assert.Equal(RedirectMatch.Exact, options.Redirects[0].Match);
        Assert.Equal(301u, options.Redirects[0].Status);
        Assert.Equal(RedirectMatch.Prefix, options.Redirects[1].Match);
        Assert.Equal("https://example.org/blog/", options.Redirects[1].Target);
        var user = Assert.Single(options.BasicAuth.Users);
        Assert.Equal("alice", user.Name);
        Assert.Equal("$2y$12$hash", user.PasswordHash);
        Assert.Equal(IpAccessMode.DenyList, options.IpAccess.Mode);
        Assert.Equal(["198.51.100.0/24"], options.IpAccess.Cidrs);
        Assert.Equal(25u, options.RequestRatePerSecond);
        Assert.Equal(50u, options.RequestBurst);
    }

    /// <summary>No options write the message's zero values and leave the optional messages absent.</summary>
    [Fact]
    public void No_options_write_the_messages_zero_values_and_leave_the_optional_messages_absent()
    {
        var descriptor = new SiteDescriptor(["a.example.com"], SiteBackendKind.Static, string.Empty, string.Empty, SiteNodeApp.None, SiteWebServer.Nginx, false, SiteOptionsDescriptor.None);

        var options = descriptor.ToWire().Options;

        Assert.Equal(CanonicalHost.Unspecified, options.CanonicalHost);
        Assert.Equal(0u, options.ClientMaxBodySizeMib);
        Assert.Equal(0u, options.StaticCacheDays);
        Assert.Empty(options.Redirects);
        // An empty BasicAuth message would read to the agent as a malformed one, so none is sent.
        Assert.Null(options.BasicAuth);
        Assert.Null(options.IpAccess);
        // Both zero: the agent reads a zero rate as "no limit" and refuses a burst that has no rate.
        Assert.Equal(0u, options.RequestRatePerSecond);
        Assert.Equal(0u, options.RequestBurst);
    }

    /// <summary>A user prints without the hash, so a log line formatting it carries no credential material.</summary>
    [Fact]
    public void A_user_prints_without_the_hash_so_a_log_line_formatting_it_carries_no_credential_material()
    {
        var text = new BasicAuthUserDescriptor("alice", "$2y$12$verysecrethash").ToString();

        Assert.Contains("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("verysecrethash", text, StringComparison.Ordinal);
    }
}
