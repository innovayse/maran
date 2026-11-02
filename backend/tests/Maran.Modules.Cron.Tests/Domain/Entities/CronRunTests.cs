using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.Enums;

namespace Maran.Modules.Cron.Tests.Domain.Entities;

/// <summary>The invariants a recorded run holds by construction.</summary>
public sealed class CronRunTests
{
    /// <summary>A run a ceiling stopped carries no status, whatever the caller passed.</summary>
    [Fact]
    public void A_run_a_ceiling_stopped_carries_no_status()
    {
        var run = new CronRun(Guid.NewGuid(), Guid.NewGuid(), "e", DateTimeOffset.UnixEpoch, 1, 0, CronRunLimit.Output, "", "");

        Assert.Null(run.ExitCode);
        Assert.Equal(CronRunOutcome.Stopped, run.Outcome);
    }
}
