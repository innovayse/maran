using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>What <see cref="PlanLimits"/> says about itself: it is a value, compared by its figures.</summary>
public sealed class PlanLimitsTests
{
    /// <summary>Two sets of limits with the same figures are equal.</summary>
    [Fact]
    public void Limits_with_the_same_figures_are_equal()
    {
        var first = ResellerTestData.Limits(diskQuotaMb: 500, features: AccountFeature.Mail);
        var second = ResellerTestData.Limits(diskQuotaMb: 500, features: AccountFeature.Mail);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    /// <summary>Limits differing in one figure are not equal.</summary>
    [Fact]
    public void Limits_differing_in_one_figure_are_not_equal()
    {
        var first = ResellerTestData.Limits(diskQuotaMb: 500);
        var second = ResellerTestData.Limits(diskQuotaMb: 501);

        Assert.NotEqual(first, second);
    }

    /// <summary>Limits carry every figure they were given under its own name.</summary>
    [Fact]
    public void Limits_carry_every_figure_they_were_given()
    {
        var runtime = RuntimeLimits.Of(ResourceLimit.Limited(1), ResourceLimit.Limited(2), ResourceLimit.Limited(3));

        var limits = new PlanLimits(10, 20, 30, 40, 50, 60, 70, AccountFeature.Dns, runtime, 80, 90);

        Assert.Equal(10, limits.DiskQuotaMb);
        Assert.Equal(20, limits.MaxSites);
        Assert.Equal(30, limits.MaxDatabases);
        Assert.Equal(40, limits.MaxSftpUsers);
        Assert.Equal(50, limits.MaxFtpUsers);
        Assert.Equal(60, limits.MaxCronEntries);
        Assert.Equal(70, limits.MaxPhpWorkersPerPool);
        Assert.Equal(AccountFeature.Dns, limits.Features);
        Assert.Equal(runtime, limits.RuntimeLimits);
        Assert.Equal(80, limits.MaxMailboxes);
        Assert.Equal(90, limits.MailboxQuotaPoolMib);
    }
}
