using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.ValueObjects;

namespace Maran.Modules.Cron.Tests.Domain.Entities;

/// <summary>The invariants a recorded run holds by construction.</summary>
public sealed class CronRunTests
{
    /// <summary>A run a ceiling stopped carries its ceiling and no status.</summary>
    [Fact]
    public void A_run_a_ceiling_stopped_carries_its_ceiling_and_no_status()
    {
        var run = Record(new CronRunStopped(CronRunLimit.Output));

        Assert.Equal(CronRunEndKind.Stopped, run.EndKind);
        Assert.Null(run.EndExitCode);
        Assert.Equal(CronRunLimit.Output, run.EndLimit);
        Assert.Equal(new CronRunStopped(CronRunLimit.Output), run.End);
        Assert.Equal(CronRunOutcome.Stopped, run.Outcome);
    }

    /// <summary>A run that exited carries its status and no ceiling.</summary>
    [Fact]
    public void A_run_that_exited_carries_its_status_and_no_ceiling()
    {
        var run = Record(new CronRunExited(0));

        Assert.Equal(CronRunEndKind.Exited, run.EndKind);
        Assert.Equal(0, run.EndExitCode);
        Assert.Null(run.EndLimit);
        Assert.Equal(new CronRunExited(0), run.End);
        Assert.Equal(CronRunOutcome.Succeeded, run.Outcome);
    }

    /// <summary>A run that ended with no status is restored as exactly that, not as an exit of zero.</summary>
    [Fact]
    public void A_run_that_ended_with_no_status_is_restored_as_exactly_that()
    {
        var run = Record(new CronRunEndedWithoutStatus());

        Assert.Equal(CronRunEndKind.WithoutStatus, run.EndKind);
        Assert.Null(run.EndExitCode);
        Assert.Null(run.EndLimit);
        Assert.Equal(new CronRunEndedWithoutStatus(), run.End);
        Assert.Equal(CronRunOutcome.Failed, run.Outcome);
    }

    /// <summary>Records a run that ended as <paramref name="end"/>.</summary>
    /// <param name="end">How the run ended.</param>
    /// <returns>The recorded run.</returns>
    private static CronRun Record(CronRunEnd end)
    {
        return new CronRun(Guid.NewGuid(), Guid.NewGuid(), "e", DateTimeOffset.UnixEpoch, 1, end, "", "");
    }
}
