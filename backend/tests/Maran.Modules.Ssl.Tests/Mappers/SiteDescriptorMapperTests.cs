using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Ssl.Mappers;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Tests.Mappers;

/// <summary>
/// Installing or removing a certificate rewrites the vhost, so the descriptor it sends has to carry the
/// site's options: without them an action that had nothing to do with access control would strip a
/// site of its redirects, credentials and IP rules.
/// </summary>
public sealed class SiteDescriptorMapperTests
{
    /// <summary>Every option the snapshot carries is carried into the descriptor.</summary>
    [Fact]
    public void Every_option_the_snapshot_carries_is_carried_into_the_descriptor()
    {
        var snapshot = Snapshot(new SiteOptionsSnapshot(
            SiteCanonicalHost.Www,
            64,
            30,
            [new SiteRedirectSnapshot("/blog/", SiteRedirectMatch.Prefix, "/news/", 302)],
            [new SiteBasicAuthUserSnapshot("alice", "$2y$12$hash")],
            new SiteIpAccessSnapshot(SiteIpAccessMode.DenyList, ["198.51.100.0/24"]),
            new SiteRequestRateSnapshot(10, 20)));

        var options = SiteDescriptorMapper.From(snapshot, hasCertificate: true, SiteWebServer.Nginx).Options;

        Assert.Equal(CanonicalHostKind.Www, options.CanonicalHost);
        Assert.Equal(64u, options.ClientMaxBodySizeMib);
        Assert.Equal(30u, options.StaticCacheDays);
        var redirect = Assert.Single(options.Redirects);
        Assert.Equal(RedirectMatchKind.Prefix, redirect.Match);
        Assert.Equal(302u, redirect.StatusCode);
        Assert.Equal("$2y$12$hash", Assert.Single(options.BasicAuthUsers).PasswordHash);
        Assert.Equal(IpAccessKind.DenyList, options.IpAccess!.Mode);
        Assert.Equal(["198.51.100.0/24"], options.IpAccess.Cidrs);
        Assert.Equal(new RequestRateDescriptor(10, 20), options.RequestRate);
    }

    /// <summary>The descriptor carries the web server it is given.</summary>
    [Theory]
    [InlineData(SiteWebServer.Nginx)]
    [InlineData(SiteWebServer.Apache)]
    public void The_descriptor_carries_the_web_server_it_is_given(SiteWebServer webServer)
    {
        // The wire reads an unset web server as nginx, so a descriptor that lost it would rewrite an Apache
        // site's vhost for nginx at the moment TLS was switched on.
        var descriptor = SiteDescriptorMapper.From(Snapshot(SiteOptionsSnapshot.None), hasCertificate: true, webServer);

        Assert.Equal(webServer, descriptor.WebServer);
    }

    /// <summary>The options travel whether the certificate is being installed or removed.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_options_travel_whether_the_certificate_is_being_installed_or_removed(bool hasCertificate)
    {
        var snapshot = Snapshot(SiteOptionsSnapshot.None with
        {
            BasicAuthUsers = [new SiteBasicAuthUserSnapshot("alice", "$2y$12$hash")],
        });

        var descriptor = SiteDescriptorMapper.From(snapshot, hasCertificate, SiteWebServer.Nginx);

        Assert.Equal(hasCertificate, descriptor.HasCertificate);
        Assert.Equal("alice", Assert.Single(descriptor.Options.BasicAuthUsers).Name);
    }

    /// <summary>
    /// Installing or removing a certificate keeps a site's request limit: a descriptor built without it
    /// would rewrite the vhost unlimited on an action that had nothing to do with the limit.
    /// </summary>
    /// <param name="hasCertificate">Whether the certificate is being installed or removed.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Installing_or_removing_a_certificate_keeps_the_sites_request_limit(bool hasCertificate)
    {
        var snapshot = Snapshot(SiteOptionsSnapshot.None with { RequestRate = new SiteRequestRateSnapshot(5, 0) });

        var options = SiteDescriptorMapper.From(snapshot, hasCertificate, SiteWebServer.Nginx).Options;

        Assert.Equal(new RequestRateDescriptor(5, 0), options.RequestRate);
    }

    /// <summary>A site with no options is described with none.</summary>
    [Fact]
    public void A_site_with_no_options_is_described_with_none()
    {
        var options = SiteDescriptorMapper.From(Snapshot(SiteOptionsSnapshot.None), hasCertificate: true, SiteWebServer.Nginx).Options;

        Assert.Equal(CanonicalHostKind.None, options.CanonicalHost);
        Assert.Null(options.ClientMaxBodySizeMib);
        Assert.Empty(options.Redirects);
        Assert.Empty(options.BasicAuthUsers);
        Assert.Null(options.IpAccess);
        Assert.Null(options.RequestRate);
    }

    /// <summary>A node site keeps its runtime facts through a certificate rewrite, and a php site carries none.</summary>
    [Fact]
    public void A_node_site_keeps_its_runtime_facts_through_a_certificate_rewrite()
    {
        // A rewrite that dropped them would replace the site's unit with an empty one.
        var node = new SiteSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "app.example.com",
            [],
            SiteBackend.Node,
            string.Empty,
            string.Empty,
            new SiteNodeSnapshot("22", 3100, "npm start"),
            false,
            SiteOptionsSnapshot.None);

        var descriptor = SiteDescriptorMapper.From(node, hasCertificate: true, SiteWebServer.Nginx);

        Assert.Equal(SiteBackendKind.Node, descriptor.Backend);
        Assert.Equal(new SiteNodeApp("22", 3100, "npm start"), descriptor.Node);
        Assert.Equal(SiteNodeApp.None, SiteDescriptorMapper.From(Snapshot(SiteOptionsSnapshot.None), hasCertificate: true, SiteWebServer.Nginx).Node);
    }

    /// <summary>Builds a snapshot of a PHP site carrying the given options.</summary>
    /// <param name="options">The options the snapshot carries.</param>
    private static SiteSnapshot Snapshot(SiteOptionsSnapshot options)
    {
        return new SiteSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), "example.com", ["www.example.com"], SiteBackend.Php, "8.3", string.Empty, SiteNodeSnapshot.None, false, options);
    }
}
