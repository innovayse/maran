using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Domain.Entities;

/// <summary>The states of a whole-server migration job (issue #197, phase 2).</summary>
public sealed class MigrationJobTests
{
    /// <summary>The moment every case runs at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A new draft job.</summary>
    /// <returns>The job.</returns>
    private static MigrationJob NewJob()
    {
        return new MigrationJob(Guid.NewGuid(), Guid.NewGuid(), "Old server", 2, null, null, null, Guid.NewGuid(), Now);
    }

    /// <summary>A job starts as a draft, which is the only editable state.</summary>
    [Fact]
    public void A_new_job_is_an_editable_draft()
    {
        var job = NewJob();

        Assert.Equal(MigrationJobState.Draft, job.State);
        Assert.True(job.IsEditable);
        Assert.False(job.IsRunning);
    }

    /// <summary>Start, pause and resume walk the states; the inverse of each is refused with a conflict.</summary>
    [Fact]
    public void Start_pause_and_resume_move_the_state_and_refuse_the_wrong_order()
    {
        var job = NewJob();

        Assert.False(job.Pause().IsSuccess);
        Assert.True(job.Start(Now).IsSuccess);
        Assert.True(job.IsRunning);
        Assert.False(job.Start(Now).IsSuccess);
        Assert.False(job.Resume().IsSuccess);
        Assert.True(job.Pause().IsSuccess);
        Assert.Equal(MigrationJobState.Paused, job.State);
        Assert.True(job.Resume().IsSuccess);
        Assert.Equal(MigrationJobState.Running, job.State);
    }

    /// <summary>A refusal of a transition is a conflict, never a validation error or a success.</summary>
    [Fact]
    public void A_refused_transition_is_a_conflict()
    {
        var refused = NewJob().Pause();

        Assert.Equal(ErrorType.Conflict, refused.Error!.Type);
        Assert.Equal("MigrationJobStateInvalid", refused.Error.Code);
    }

    /// <summary>Completing is idempotent and only a running job completes.</summary>
    [Fact]
    public void Only_a_running_job_completes_and_asking_twice_changes_nothing()
    {
        var draft = NewJob();
        draft.Complete(Now);
        Assert.Equal(MigrationJobState.Draft, draft.State);

        var running = NewJob();
        running.Start(Now);
        running.Complete(Now.AddHours(1));
        running.Complete(Now.AddHours(2));

        Assert.Equal(MigrationJobState.Completed, running.State);
        Assert.Equal(Now.AddHours(1), running.FinishedAt);
    }

    /// <summary>A finished job cannot be cancelled again, and a cancelled one stays cancelled.</summary>
    [Fact]
    public void Cancelling_ends_the_job_once()
    {
        var job = NewJob();

        Assert.True(job.Cancel(Now).IsSuccess);
        Assert.False(job.Cancel(Now).IsSuccess);
        Assert.Equal(MigrationJobState.Cancelled, job.State);
    }

    /// <summary>The limits accept 1 to 8 accounts and a positive bandwidth, and refuse everything else without changing the job.</summary>
    [Theory]
    [InlineData(0, null)]
    [InlineData(9, null)]
    [InlineData(2, 0)]
    [InlineData(2, -5)]
    [InlineData(2, 1_048_577)]
    public void Limits_outside_the_bounds_are_refused_and_change_nothing(int concurrency, int? bandwidth)
    {
        var job = NewJob();

        var refused = job.ChangeLimits(concurrency, bandwidth);

        Assert.Equal("MigrationLimitInvalid", refused.Error!.Code);
        Assert.Equal(2, job.Concurrency);
        Assert.Null(job.BandwidthLimitKib);
    }

    /// <summary>The inverse: limits inside the bounds are stored, while the job runs too.</summary>
    [Fact]
    public void Limits_inside_the_bounds_are_stored_while_the_job_runs()
    {
        var job = NewJob();
        job.Start(Now);

        Assert.True(job.ChangeLimits(8, 2048).IsSuccess);

        Assert.Equal(8, job.Concurrency);
        Assert.Equal(2048, job.BandwidthLimitKib);
    }
}
