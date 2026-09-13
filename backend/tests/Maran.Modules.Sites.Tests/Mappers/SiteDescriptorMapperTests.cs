using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Mappers;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="SiteDescriptorMapper"/>: every field of the descriptor comes
/// from the stored row, and none is a literal.
/// </summary>
/// <remarks>
/// This suite exists because the factory's own doc comment names a specific defect it prevents —
/// a literal <c>false</c> for <c>HasCertificate</c> silently dropping a live site back to plain
/// HTTP on an unrelated edit — and a protection whose defeat changes no test is a protection in
/// name only. Each field is asserted with a value a literal could not accidentally match.
/// </remarks>
public sealed class SiteDescriptorMapperTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>An installed certificate is carried into the descriptor.</summary>
    [Fact]
    public void An_installed_certificate_is_carried_into_the_descriptor()
    {
        var site = PhpSite();
        site.AttachCertificate();

        var descriptor = SiteDescriptorMapper.From(site);

        Assert.True(descriptor.HasCertificate);
    }

    /// <summary>A site with no certificate is described as having none.</summary>
    [Fact]
    public void A_site_with_no_certificate_is_described_as_having_none()
    {
        // The other direction, so the field is read rather than hardcoded either way: a literal
        // `true` would be caught here and a literal `false` by the test above.
        var descriptor = SiteDescriptorMapper.From(PhpSite());

        Assert.False(descriptor.HasCertificate);
    }

    /// <summary>A removed certificate is carried into the descriptor as absent.</summary>
    [Fact]
    public void A_removed_certificate_is_carried_into_the_descriptor_as_absent()
    {
        var site = PhpSite();
        site.AttachCertificate();
        site.DetachCertificate();

        var descriptor = SiteDescriptorMapper.From(site);

        Assert.False(descriptor.HasCertificate);
    }

    /// <summary>The upstream of a reverse proxy site is carried into the descriptor.</summary>
    [Fact]
    public void The_upstream_of_a_reverse_proxy_site_is_carried_into_the_descriptor()
    {
        // A fabricated upstream here would silently repoint a live site at somebody else's backend
        // on the next unrelated re-render.
        var site = new Site(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "proxy.example.com",
            [],
            SiteBackendType.ReverseProxy,
            string.Empty,
            "127.0.0.1:8080",
            "/home/acct/sites/proxy.example.com",
            CreatedAt);

        var descriptor = SiteDescriptorMapper.From(site);

        Assert.Equal("127.0.0.1:8080", descriptor.ProxyUpstream);
        Assert.Equal(SiteBackendKind.ReverseProxy, descriptor.Backend);
    }

    /// <summary>A static site is described as static with no php version.</summary>
    [Fact]
    public void A_static_site_is_described_as_static_with_no_php_version()
    {
        var site = new Site(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "static.example.com",
            [],
            SiteBackendType.Static,
            string.Empty,
            string.Empty,
            "/home/acct/sites/static.example.com",
            CreatedAt);

        var descriptor = SiteDescriptorMapper.From(site);

        Assert.Equal(SiteBackendKind.Static, descriptor.Backend);
        Assert.Equal(string.Empty, descriptor.PhpVersion);
    }

    /// <summary>The stored aliases and php version are carried into the descriptor.</summary>
    [Fact]
    public void The_stored_aliases_and_php_version_are_carried_into_the_descriptor()
    {
        var descriptor = SiteDescriptorMapper.From(PhpSite());

        Assert.Equal(["www.example.com", "cdn.example.com"], descriptor.Aliases);
        Assert.Equal("8.3", descriptor.PhpVersion);
    }

    /// <summary>Builds a PHP-backed site with two aliases.</summary>
    private static Site PhpSite()
    {
        return new Site(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example.com",
            ["www.example.com", "cdn.example.com"],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "/home/acct/sites/example.com",
            CreatedAt);
    }
}
