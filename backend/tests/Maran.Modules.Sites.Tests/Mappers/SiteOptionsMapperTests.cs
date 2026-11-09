using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="SiteOptionsMapper"/> and <see cref="SiteOptionsSnapshotMapper"/>.</summary>
public sealed class SiteOptionsMapperTests
{
    /// <summary>The outward view lists users by name and carries no hash anywhere.</summary>
    [Fact]
    public void The_outward_view_lists_users_by_name_and_carries_no_hash_anywhere()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Full());

        var dto = SiteOptionsMapper.From(site, SitesTestContext.SiteNames());

        Assert.Equal(["alice"], dto.BasicAuthUsers);
        Assert.DoesNotContain(SiteOptionsSamples.StoredHash, System.Text.Json.JsonSerializer.Serialize(dto), StringComparison.Ordinal);
        Assert.Equal(site.Id, dto.SiteId);
        Assert.Equal(64, dto.ClientMaxBodySizeMib);
        Assert.Equal(["203.0.113.0/24"], dto.IpRules);
        Assert.Equal(new SiteRequestRateDto(10, 20), dto.RequestRate);
    }

    /// <summary>A site with no options is shown as the empty set, with its bounds and choices.</summary>
    [Fact]
    public void A_site_with_no_options_is_shown_as_the_empty_set_with_its_bounds_and_choices()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        var dto = SiteOptionsMapper.From(site, SitesTestContext.SiteNames());

        Assert.Equal(CanonicalHost.None, dto.CanonicalHost);
        Assert.Null(dto.ClientMaxBodySizeMib);
        Assert.Null(dto.IpAccessMode);
        Assert.Empty(dto.Redirects);
        Assert.Empty(dto.BasicAuthUsers);
        Assert.Empty(dto.IpRules);
        Assert.Null(dto.RequestRate);
        Assert.Equal(SiteOptionsLimits.MaximumRedirects, dto.Limits.MaximumRedirects);
        Assert.Equal(SiteOptionsLimits.MinimumRequestsPerSecond, dto.Limits.MinimumRequestsPerSecond);
        Assert.Equal(SiteOptionsLimits.MaximumRequestsPerSecond, dto.Limits.MaximumRequestsPerSecond);
        Assert.Equal(SiteOptionsLimits.MinimumRequestBurst, dto.Limits.MinimumRequestBurst);
        Assert.Equal(SiteOptionsLimits.MaximumRequestBurst, dto.Limits.MaximumRequestBurst);
        Assert.Equal(SiteOptionsLimits.MaximumBodySizeMib, dto.Limits.MaximumBodySizeMib);
        Assert.Equal(["none", "www", "apex"], dto.Choices.CanonicalHosts.Select(choice =>
        {
            return choice.Value;
        }));
        Assert.Equal(["exact", "prefix"], dto.Choices.RedirectMatches.Select(choice =>
        {
            return choice.Value;
        }));
        Assert.Equal(["permanent", "temporary"], dto.Choices.RedirectStatuses.Select(choice =>
        {
            return choice.Value;
        }));
        Assert.Equal(["allowList", "denyList"], dto.Choices.IpAccessModes.Select(choice =>
        {
            return choice.Value;
        }));
    }

    /// <summary>Every choice has a label that is not its own resource key.</summary>
    [Fact]
    public void Every_choice_has_a_label_that_is_not_its_own_resource_key()
    {
        // vue-i18n-style failure: a missing resx entry makes the localizer return the KEY, so the
        // customer would read "SiteCanonicalHostApex" where a label belongs.
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");

        var choices = SiteOptionsMapper.From(site, SitesTestContext.SiteNames()).Choices;

        var all = choices.CanonicalHosts
            .Concat(choices.RedirectMatches)
            .Concat(choices.RedirectStatuses)
            .Concat(choices.IpAccessModes)
            .ToList();
        Assert.All(all, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Label));
            Assert.DoesNotMatch("^Site[A-Za-z]+$", choice.Label);
        });
    }

    /// <summary>The snapshot another module reads carries every stored group.</summary>
    [Fact]
    public void The_snapshot_another_module_reads_carries_every_stored_group()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ReplaceOptions(SiteOptionsSamples.Full());

        var snapshot = SiteOptionsSnapshotMapper.From(site.Options);

        Assert.Equal(Maran.Sdk.Contracts.SiteCanonicalHost.Www, snapshot.CanonicalHost);
        Assert.Equal(64, snapshot.ClientMaxBodySizeMib);
        Assert.Equal(30, snapshot.StaticCacheDays);
        Assert.Equal(301, Assert.Single(snapshot.Redirects).StatusCode);
        Assert.Equal(SiteOptionsSamples.StoredHash, Assert.Single(snapshot.BasicAuthUsers).PasswordHash);
        Assert.Equal(Maran.Sdk.Contracts.SiteIpAccessMode.AllowList, snapshot.IpAccess!.Mode);
        Assert.Equal(new Maran.Sdk.Contracts.SiteRequestRateSnapshot(10, 20), snapshot.RequestRate);
    }

    /// <summary>A site with no options reads as the empty snapshot.</summary>
    [Fact]
    public void A_site_with_no_options_reads_as_the_empty_snapshot()
    {
        var snapshot = SiteOptionsSnapshotMapper.From(null);

        Assert.Equal(Maran.Sdk.Contracts.SiteCanonicalHost.None, snapshot.CanonicalHost);
        Assert.Empty(snapshot.Redirects);
        Assert.Empty(snapshot.BasicAuthUsers);
        Assert.Null(snapshot.IpAccess);
        Assert.Null(snapshot.RequestRate);
    }
}
