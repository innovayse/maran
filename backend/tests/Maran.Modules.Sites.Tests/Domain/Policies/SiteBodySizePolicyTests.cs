using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>Behavioral contract of <see cref="SiteBodySizePolicy"/> (issue #556).</summary>
public sealed class SiteBodySizePolicyTests
{
    /// <summary>A plan bounding uploads at 100 MiB.</summary>
    private static readonly PhpLimitsSnapshot Plan = new(256, 100, 120, 3000);

    /// <summary>A PHP site with no stated figure takes the plan's bound.</summary>
    [Fact]
    public void A_php_site_with_no_stated_figure_takes_the_plans_bound()
    {
        Assert.Equal(100, SiteBodySizePolicy.Effective(SiteBackendType.Php, null, Plan));
    }

    /// <summary>A stated figure within the bound is kept.</summary>
    [Fact]
    public void A_stated_figure_within_the_bound_is_kept()
    {
        Assert.Equal(20, SiteBodySizePolicy.Effective(SiteBackendType.Php, 20, Plan));
    }

    /// <summary>A figure equal to the bound is kept (the edge is inclusive).</summary>
    [Fact]
    public void A_figure_equal_to_the_bound_is_kept()
    {
        Assert.Equal(100, SiteBodySizePolicy.Effective(SiteBackendType.Php, 100, Plan));
        Assert.False(SiteBodySizePolicy.ExceedsPlan(SiteBackendType.Php, 100, Plan));
    }

    /// <summary>A figure above the bound is cut to it and counts as exceeding the plan.</summary>
    [Fact]
    public void A_figure_above_the_bound_is_cut_to_it_and_exceeds_the_plan()
    {
        Assert.Equal(100, SiteBodySizePolicy.Effective(SiteBackendType.Php, 101, Plan));
        Assert.True(SiteBodySizePolicy.ExceedsPlan(SiteBackendType.Php, 101, Plan));
    }

    /// <summary>A bound above the largest limit a site may carry is cut to that limit.</summary>
    [Fact]
    public void A_bound_above_the_largest_limit_a_site_may_carry_is_cut_to_that_limit()
    {
        var huge = new PhpLimitsSnapshot(256, 5000, 120, 3000);

        Assert.Equal(SiteOptionsLimits.MaximumBodySizeMib, SiteBodySizePolicy.Effective(SiteBackendType.Php, null, huge));
    }

    /// <summary>A site that is not PHP keeps its stated figure and gains none from the plan.</summary>
    [Fact]
    public void A_site_that_is_not_php_keeps_its_stated_figure_and_gains_none_from_the_plan()
    {
        foreach (var backend in new[] { SiteBackendType.Static, SiteBackendType.ReverseProxy, SiteBackendType.Node })
        {
            Assert.Null(SiteBodySizePolicy.Effective(backend, null, Plan));
            Assert.Equal(900, SiteBodySizePolicy.Effective(backend, 900, Plan));
            Assert.False(SiteBodySizePolicy.ExceedsPlan(backend, 900, Plan));
        }
    }

    /// <summary>No stated figure never exceeds the plan.</summary>
    [Fact]
    public void No_stated_figure_never_exceeds_the_plan()
    {
        Assert.False(SiteBodySizePolicy.ExceedsPlan(SiteBackendType.Php, null, Plan));
    }
}
