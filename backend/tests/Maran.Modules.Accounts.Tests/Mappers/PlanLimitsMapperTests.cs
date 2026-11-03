using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="PlanLimitsMapper"/>: every figure a plan definition states is
/// copied, none is judged, so the envelope policy sees exactly what the plan would sell.
/// </summary>
public sealed class PlanLimitsMapperTests
{
    /// <summary>Every figure of the definition is copied, the runtime ceilings and the features included.</summary>
    [Fact]
    public void Every_figure_of_the_definition_is_copied()
    {
        var definition = new CreatePlanCommand(
            "Agency",
            DiskQuotaMb: 1_000,
            MaxSites: 2,
            MaxDatabases: 3,
            MaxSftpUsers: 4,
            MaxFtpUsers: 5,
            MaxCronEntries: 6,
            MaxPhpWorkersPerPool: 7,
            CpuQuotaPercent: ResourceCeilingTestValues.Limited(150),
            MemoryMaxMb: ResourceCeilingTestValues.Limited(2_048),
            MaxProcesses: ResourceCeilingTestValues.Unlimited,
            Features: [AccountFeature.Mail, AccountFeature.Cron]);

        var limits = PlanLimitsMapper.ToLimits(definition);

        Assert.Equal(1_000, limits.DiskQuotaMb);
        Assert.Equal(2, limits.MaxSites);
        Assert.Equal(3, limits.MaxDatabases);
        Assert.Equal(4, limits.MaxSftpUsers);
        Assert.Equal(5, limits.MaxFtpUsers);
        Assert.Equal(6, limits.MaxCronEntries);
        Assert.Equal(7, limits.MaxPhpWorkersPerPool);
        Assert.Equal(AccountFeature.Mail | AccountFeature.Cron, limits.Features);
        Assert.Equal(150, limits.RuntimeLimits.CpuQuotaPercent.Maximum);
        Assert.Equal(2_048, limits.RuntimeLimits.MemoryMaxMb.Maximum);
        Assert.Null(limits.RuntimeLimits.MaxProcesses.Maximum);
    }

    /// <summary>A definition selling no feature restates the empty set.</summary>
    [Fact]
    public void A_definition_selling_no_feature_restates_the_empty_set()
    {
        var definition = new CreatePlanCommand(
            "Bare", 1, 1, 1, 1, 1, 1, 1,
            ResourceCeilingTestValues.Unlimited,
            ResourceCeilingTestValues.Unlimited,
            ResourceCeilingTestValues.Unlimited,
            []);

        Assert.Equal(AccountFeature.None, PlanLimitsMapper.ToLimits(definition).Features);
    }
}
