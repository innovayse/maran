using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.Policies;
using Maran.Modules.Cron.Domain.ValueObjects;

namespace Maran.Modules.Cron.Tests.Domain.Policies;

/// <summary>How a run's outcome is decided from how it ended.</summary>
public sealed class CronRunOutcomePolicyTests
{
    /// <summary>Only a zero the program returned is a success.</summary>
    [Fact]
    public void Only_a_zero_the_program_returned_is_a_success()
    {
        Assert.Equal(CronRunOutcome.Succeeded, CronRunOutcomePolicy.Of(new CronRunExited(0)));
        Assert.Equal(CronRunOutcome.Failed, CronRunOutcomePolicy.Of(new CronRunExited(1)));
        Assert.Equal(CronRunOutcome.Failed, CronRunOutcomePolicy.Of(new CronRunEndedWithoutStatus()));
    }

    /// <summary>A ceiling stops the run, whichever ceiling it was.</summary>
    [Fact]
    public void A_ceiling_stops_the_run_whichever_ceiling_it_was()
    {
        Assert.Equal(CronRunOutcome.Stopped, CronRunOutcomePolicy.Of(new CronRunStopped(CronRunLimit.Runtime)));
        Assert.Equal(CronRunOutcome.Stopped, CronRunOutcomePolicy.Of(new CronRunStopped(CronRunLimit.Output)));
    }
}
