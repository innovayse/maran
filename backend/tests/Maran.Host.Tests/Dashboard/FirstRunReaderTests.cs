using Maran.Host.Dashboard;

namespace Maran.Host.Tests.Dashboard;

/// <summary>
/// Covers <see cref="FirstRunReader.Assemble"/>: a step is done when its count is above zero, a step
/// that could not be read is unavailable and never "not done", and the checklist is complete only
/// when every step was read and is done.
/// </summary>
public sealed class FirstRunReaderTests
{
    /// <summary>The ticks expected when only the second and fourth steps have data.</summary>
    private static readonly bool[] SecondAndFourthDone = [false, true, false, true, false];

    /// <summary>A server where nothing was made yet has five steps, none of them done and none complete.</summary>
    [Fact]
    public void A_fresh_server_has_every_step_open_and_is_not_complete()
    {
        var checklist = Assemble(0, 0, 0, 0, 0);

        Assert.Equal(5, checklist.Steps.Count);
        Assert.All(checklist.Steps, step => { Assert.False(DoneOf(step)); });
        Assert.False(checklist.Complete);
    }

    /// <summary>The steps come in the order the card lists them, each pointing at its own screen.</summary>
    [Fact]
    public void The_steps_keep_their_order_and_point_at_their_screens()
    {
        var checklist = Assemble(0, 0, 0, 0, 0);

        Assert.Equal(
            new[]
            {
                (FirstRunStepKey.Plan, FirstRunRoute.Plans),
                (FirstRunStepKey.Account, FirstRunRoute.Accounts),
                (FirstRunStepKey.Site, FirstRunRoute.Sites),
                (FirstRunStepKey.BackupDestination, FirstRunRoute.BackupDestinations),
                (FirstRunStepKey.Certificate, FirstRunRoute.Certificates),
            },
            checklist.Steps.Select(step => { return (step.Key, step.RouteName); }).ToArray());
    }

    /// <summary>Each count ticks only its own step, so no step is ticked by another's data.</summary>
    [Fact]
    public void Each_count_ticks_only_its_own_step()
    {
        var checklist = Assemble(0, 3, 0, 1, 0);

        Assert.Equal(SecondAndFourthDone, checklist.Steps.Select(DoneOf).ToArray());
        Assert.False(checklist.Complete);
    }

    /// <summary>Every step done completes the checklist (the inverse control of the cases above).</summary>
    [Fact]
    public void Every_step_done_completes_the_checklist()
    {
        var checklist = Assemble(1, 2, 3, 1, 5);

        Assert.All(checklist.Steps, step => { Assert.True(DoneOf(step)); });
        Assert.True(checklist.Complete);
    }

    /// <summary>A step whose module could not answer is unavailable, never "not done", and keeps the checklist open.</summary>
    [Fact]
    public void A_step_that_could_not_be_read_is_unavailable_and_blocks_completion()
    {
        var checklist = FirstRunReader.Assemble(
            DashboardSection.Loaded(1),
            DashboardSection.Loaded(1),
            DashboardSection.Unavailable<int>(),
            DashboardSection.Loaded(1),
            DashboardSection.Loaded(1));

        var site = checklist.Steps.Single(step => { return step.Key == FirstRunStepKey.Site; });
        Assert.Equal(DashboardSectionState.Unavailable, site.Done.State);
        Assert.False(checklist.Complete);
    }

    /// <summary>Assembles a checklist from five loaded counts.</summary>
    /// <param name="plans">Custom plans.</param>
    /// <param name="accounts">Accounts.</param>
    /// <param name="sites">Sites.</param>
    /// <param name="backups">Configured backup destinations and schedules.</param>
    /// <param name="certificates">Certificates.</param>
    /// <returns>The checklist.</returns>
    private static FirstRunChecklistDto Assemble(int plans, int accounts, int sites, int backups, int certificates)
    {
        return FirstRunReader.Assemble(
            DashboardSection.Loaded(plans),
            DashboardSection.Loaded(accounts),
            DashboardSection.Loaded(sites),
            DashboardSection.Loaded(backups),
            DashboardSection.Loaded(certificates));
    }

    /// <summary>Reads a step known to be loaded.</summary>
    /// <param name="step">The step.</param>
    /// <returns>Whether it is done.</returns>
    private static bool DoneOf(FirstRunStepDto step)
    {
        return step.Done.Match(
            done =>
            {
                return done;
            },
            () =>
            {
                throw new InvalidOperationException("The step was expected to be loaded.");
            });
    }
}
