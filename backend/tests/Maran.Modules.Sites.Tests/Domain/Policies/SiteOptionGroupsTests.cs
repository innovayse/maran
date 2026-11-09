using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="SiteOptionGroups"/>: which groups a replacement changed.</summary>
public sealed class SiteOptionGroupsTests
{
    /// <summary>Two equal sets change nothing.</summary>
    [Fact]
    public void Two_equal_sets_change_nothing()
    {
        Assert.Empty(SiteOptionGroups.Changed(SiteOptionsSamples.Full(), SiteOptionsSamples.Full()));
    }

    /// <summary>Going from nothing to everything changes every group, in a fixed order.</summary>
    [Fact]
    public void Going_from_nothing_to_everything_changes_every_group_in_a_fixed_order()
    {
        Assert.Equal(
            [
                SiteOptionGroups.CanonicalHost,
                SiteOptionGroups.UploadSize,
                SiteOptionGroups.Cache,
                SiteOptionGroups.Redirects,
                SiteOptionGroups.BasicAuth,
                SiteOptionGroups.IpRules,
                SiteOptionGroups.RequestRate,
            ],
            SiteOptionGroups.Changed(SiteOptionsSamples.Empty(), SiteOptionsSamples.Full()));
    }

    /// <summary>Each group is reported alone when only it changed.</summary>
    [Fact]
    public void Each_group_is_reported_alone_when_only_it_changed()
    {
        var full = SiteOptionsSamples.Full();

        Assert.Equal([SiteOptionGroups.UploadSize], SiteOptionGroups.Changed(full, full with { ClientMaxBodySizeMib = 128 }));
        Assert.Equal([SiteOptionGroups.Cache], SiteOptionGroups.Changed(full, full with { StaticCacheDays = 1 }));
        Assert.Equal([SiteOptionGroups.IpRules], SiteOptionGroups.Changed(full, full with { IpAccessMode = IpAccessMode.DenyList }));
        Assert.Equal([SiteOptionGroups.RequestRate], SiteOptionGroups.Changed(full, full with { RequestRate = new SiteRequestRate(11, 20) }));
        Assert.Equal([SiteOptionGroups.RequestRate], SiteOptionGroups.Changed(full, full with { RequestRate = null }));
        Assert.Equal(
            [SiteOptionGroups.BasicAuth],
            SiteOptionGroups.Changed(full, full with { BasicAuthUsers = [new SiteBasicAuthUserValue("alice", "other")] }));
    }
}
