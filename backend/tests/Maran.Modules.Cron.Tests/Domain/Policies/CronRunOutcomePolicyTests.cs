using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.Policies;

namespace Maran.Modules.Cron.Tests.Domain.Policies;

/// <summary>How a run's outcome is decided from its status and its ceiling.</summary>
public sealed class CronRunOutcomePolicyTests
{
    /// <summary>Only a zero the program returned is a success.</summary>
    [Fact]
    public void Only_a_zero_the_program_returned_is_a_success()
    {
        Assert.Equal(CronRunOutcome.Succeeded, CronRunOutcomePolicy.Of(0, null));
        Assert.Equal(CronRunOutcome.Failed, CronRunOutcomePolicy.Of(1, null));
        Assert.Equal(CronRunOutcome.Failed, CronRunOutcomePolicy.Of(null, null));
    }

    /// <summary>A ceiling outranks any status.</summary>
    [Fact]
    public void A_ceiling_outranks_any_status()
    {
        Assert.Equal(CronRunOutcome.Stopped, CronRunOutcomePolicy.Of(0, CronRunLimit.Runtime));
        Assert.Equal(CronRunOutcome.Stopped, CronRunOutcomePolicy.Of(null, CronRunLimit.Output));
    }
}
