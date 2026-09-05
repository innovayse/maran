using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Tests.Domain;

/// <summary>Behavioral contract of the <see cref="Site"/> entity.</summary>
public sealed class SiteTests
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A new site is enabled and has no certificate.</summary>
    [Fact]
    public void A_new_site_is_enabled_and_has_no_certificate()
    {
        var site = NewSite();

        Assert.Equal(SiteStatus.Enabled, site.Status);
        Assert.False(site.HasCertificate);
    }

    /// <summary>A new site copies its aliases rather than aliasing the callers array.</summary>
    [Fact]
    public void A_new_site_copies_its_aliases_rather_than_aliasing_the_callers_array()
    {
        // Otherwise the caller still holds a handle on the entity's own state, and a later edit to
        // their array silently changes what the next vhost render is told the site is.
        var aliases = new[] { "www.example.com" };
        var site = new Site(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example.com",
            aliases,
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "/home/acct/sites/example.com",
            CreatedAt);

        aliases[0] = "evil.example.com";

        Assert.Equal(["www.example.com"], site.Aliases);
    }

    /// <summary>Changing the php version replaces it and touches nothing else.</summary>
    [Fact]
    public void Changing_the_php_version_replaces_it_and_touches_nothing_else()
    {
        var site = NewSite();

        site.ChangePhpVersion("8.4");

        Assert.Equal("8.4", site.PhpVersion);
        Assert.Equal(SiteStatus.Enabled, site.Status);
        Assert.Equal(SiteBackendType.Php, site.BackendType);
    }

    /// <summary>Disabling a site marks it disabled and is idempotent.</summary>
    [Fact]
    public void Disabling_a_site_marks_it_disabled_and_is_idempotent()
    {
        var site = NewSite();

        site.Disable();
        site.Disable();

        Assert.Equal(SiteStatus.Disabled, site.Status);
    }

    /// <summary>Enabling a disabled site returns it to serving and is idempotent.</summary>
    [Fact]
    public void Enabling_a_disabled_site_returns_it_to_serving_and_is_idempotent()
    {
        var site = NewSite();
        site.Disable();

        site.Enable();
        site.Enable();

        Assert.Equal(SiteStatus.Enabled, site.Status);
    }

    /// <summary>Builds a plain PHP-backed site.</summary>
    private static Site NewSite()
    {
        return new Site(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example.com",
            ["www.example.com"],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            "/home/acct/sites/example.com",
            CreatedAt);
    }
}
