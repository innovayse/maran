using Maran.Modules.Cron.Domain.Entities;
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

    /// <summary>The stored-end filter selects exactly the runs whose outcome the policy decides.</summary>
    [Fact]
    public void The_stored_end_filter_agrees_with_the_policy_for_every_ending()
    {
        CronRunEnd[] endings =
        [
            new CronRunExited(0),
            new CronRunExited(1),
            new CronRunEndedWithoutStatus(),
            new CronRunStopped(CronRunLimit.Runtime),
            new CronRunStopped(CronRunLimit.Output),
        ];
        var runs = endings
            .Select(end =>
            {
                return new CronRun(Guid.NewGuid(), Guid.NewGuid(), "e", DateTimeOffset.UnixEpoch, 1, end, "", "");
            })
            .ToList();

        foreach (var outcome in Enum.GetValues<CronRunOutcome>())
        {
            var selected = runs.Where(CronRunOutcomePolicy.Matching(outcome).Compile()).Select(run =>
            {
                return run.End;
            });
            var expected = endings.Where(end =>
            {
                return CronRunOutcomePolicy.Of(end) == outcome;
            });

            Assert.Equal(expected, selected);
            Assert.NotEmpty(expected);
        }
    }
}
