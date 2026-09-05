using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Domain;

/// <summary>
/// The three rules a panel task enforces on whatever is recording it: the percentage is clamped,
/// the log is capped and marked, and a finished task never changes again.
/// </summary>
public sealed class PanelTaskTests
{
    /// <summary>The instant every outcome in these tests is stamped with.</summary>
    private static readonly DateTimeOffset Finished = TasksTestContext.Now.AddMinutes(3);

    /// <summary>A percentage above one hundred is clamped to one hundred.</summary>
    [Fact]
    public void A_percentage_above_one_hundred_is_clamped_to_one_hundred()
    {
        var task = TasksTestContext.Row();

        task.Report(140, "ordering");

        Assert.Equal(100, task.Percent);
    }

    /// <summary>A negative percentage is clamped to zero.</summary>
    [Fact]
    public void A_negative_percentage_is_clamped_to_zero()
    {
        // The other half of the clamp, and the half a one-sided Math.Min would miss: a caller
        // computing "done / total" against a total it read as zero produces negatives just as
        // readily as it produces numbers over a hundred.
        var task = TasksTestContext.Row();

        task.Report(-7, "ordering");

        Assert.Equal(0, task.Percent);
    }

    /// <summary>A percentage inside the range is stored exactly as reported.</summary>
    [Fact]
    public void A_percentage_inside_the_range_is_stored_exactly_as_reported()
    {
        // Without this the clamp could be a constant and both tests above would still pass.
        var task = TasksTestContext.Row();

        task.Report(37, "ordering");

        Assert.Equal(37, task.Percent);
    }

    /// <summary>A log that outgrows its cap is cut and marked as cut.</summary>
    [Fact]
    public void A_log_that_outgrows_its_cap_is_cut_and_marked_as_cut()
    {
        var task = TasksTestContext.Row();

        // One line longer than the whole cap, so the very first report has to cut it.
        task.Report(10, new string('x', PanelTask.MaxLogLength + 500));

        Assert.Equal(PanelTask.MaxLogLength + PanelTask.TruncationMarker.Length, task.Log.Length);
        Assert.EndsWith(PanelTask.TruncationMarker, task.Log, StringComparison.Ordinal);
    }

    /// <summary>A log that has already been cut does not grow again.</summary>
    [Fact]
    public void A_log_that_has_already_been_cut_does_not_grow_again()
    {
        // The cap is not a one-off trim: an operation that keeps reporting must keep costing
        // nothing, and a second marker in the middle of the text would be worse than the first.
        var task = TasksTestContext.Row();
        task.Report(10, new string('x', PanelTask.MaxLogLength + 500));
        var afterFirstCut = task.Log;

        task.Report(20, "and another line");

        Assert.Equal(afterFirstCut, task.Log);
    }

    /// <summary>A log inside its cap keeps every line it was given.</summary>
    [Fact]
    public void A_log_inside_its_cap_keeps_every_line_it_was_given()
    {
        var task = TasksTestContext.Row();

        task.Report(10, "ordering");
        task.Report(20, "installing");

        Assert.Equal("ordering\ninstalling", task.Log);
    }

    /// <summary>A failure after a completion is refused and the task stays completed.</summary>
    [Fact]
    public void A_failure_after_a_completion_is_refused_and_the_task_stays_completed()
    {
        // The case the rule exists for. An operation that finished its work and then had its
        // journalling throw must not be shown to an operator as a failed operation — that reads as
        // destruction that did not happen, or as work that must be retried when it must not be.
        var task = TasksTestContext.Row();
        task.Complete(Finished);

        task.Fail("AccountCleanupFailed", Finished.AddMinutes(1));

        Assert.Equal(PanelTaskStatus.Completed, task.Status);
        Assert.Null(task.ErrorCode);
        Assert.Equal(Finished, task.FinishedAt);
    }

    /// <summary>A completion after a failure is refused and the task stays failed.</summary>
    [Fact]
    public void A_completion_after_a_failure_is_refused_and_the_task_stays_failed()
    {
        // The same rule read the other way, and the more dangerous direction: a failure overwritten
        // by a late completion hides an operation that got part-way through destroying things.
        var task = TasksTestContext.Row();
        task.Fail("AccountCleanupFailed", Finished);

        task.Complete(Finished.AddMinutes(1));

        Assert.Equal(PanelTaskStatus.Failed, task.Status);
        Assert.Equal("AccountCleanupFailed", task.ErrorCode);
    }

    /// <summary>Progress reported after a task has finished is ignored.</summary>
    [Fact]
    public void Progress_reported_after_a_task_has_finished_is_ignored()
    {
        var task = TasksTestContext.Row();
        task.Report(40, "ordering");
        task.Complete(Finished);

        task.Report(10, "a late line");

        Assert.Equal(100, task.Percent);
        Assert.Equal("ordering", task.Log);
    }

    /// <summary>A completion carries the task to one hundred percent and stamps its finish.</summary>
    [Fact]
    public void A_completion_carries_the_task_to_one_hundred_percent_and_stamps_its_finish()
    {
        var task = TasksTestContext.Row();
        task.Report(40, "ordering");

        task.Complete(Finished);

        Assert.Equal(PanelTaskStatus.Completed, task.Status);
        Assert.Equal(100, task.Percent);
        Assert.Equal(Finished, task.FinishedAt);
    }

    /// <summary>A failure records the code the operation answered its caller with.</summary>
    [Fact]
    public void A_failure_records_the_code_the_operation_answered_its_caller_with()
    {
        var task = TasksTestContext.Row();

        task.Fail("SiteNotFound", Finished);

        Assert.Equal(PanelTaskStatus.Failed, task.Status);
        Assert.Equal("SiteNotFound", task.ErrorCode);
        Assert.Equal(Finished, task.FinishedAt);
    }

    /// <summary>Every accepted change moves the revision and every refused one does not.</summary>
    [Fact]
    public void Every_accepted_change_moves_the_revision_and_every_refused_one_does_not()
    {
        // The revision is what the stream sends a frame on, so a change that did not move it is a
        // change no watcher ever sees — and a refusal that DID move it is a frame saying nothing.
        var task = TasksTestContext.Row();
        Assert.Equal(0, task.Revision);

        task.Report(20, "ordering");
        task.Complete(Finished);
        var afterOutcome = task.Revision;

        task.Fail("SiteNotFound", Finished);
        task.Report(90, "a late line");

        Assert.Equal(2, afterOutcome);
        Assert.Equal(afterOutcome, task.Revision);
    }
}
