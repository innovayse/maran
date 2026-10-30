using Maran.Modules.Tasks.Mappers;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Mappers;

/// <summary>The one decision the mapper restates: whether the reader is sent the log.</summary>
public sealed class PanelTaskMapperTests
{
    /// <summary>A reader who may not see the log is told it was withheld, sent an empty one and everything else unchanged.</summary>
    [Fact]
    public void A_reader_who_may_not_see_the_log_is_told_it_was_withheld_and_sent_everything_else_unchanged()
    {
        var task = TasksTestContext.Row(subject: "acme", accountId: Guid.NewGuid());
        task.Report(30, "tool output");

        var dto = PanelTaskMapper.From(task, "Taking a backup", includeLog: false);

        Assert.False(dto.LogIncluded);
        Assert.Equal(string.Empty, dto.Log);
        Assert.Equal(30, dto.Percent);
        Assert.Equal("acme", dto.Subject);
    }

    /// <summary>A reader who may see the log is sent it whole.</summary>
    [Fact]
    public void A_reader_who_may_see_the_log_is_sent_it_whole()
    {
        var task = TasksTestContext.Row();
        task.Report(30, "tool output");

        var dto = PanelTaskMapper.From(task, "Taking a backup", includeLog: true);

        Assert.True(dto.LogIncluded);
        Assert.Equal("tool output", dto.Log);
    }

    /// <summary>A task that has reported nothing yet is sent an empty log that is still marked as included.</summary>
    /// <remarks>
    /// The case the flag exists for: without it an administrator's empty log and a customer's
    /// withheld one are the same value on the wire.
    /// </remarks>
    [Fact]
    public void A_task_that_has_reported_nothing_yet_is_sent_an_empty_log_still_marked_as_included()
    {
        var task = TasksTestContext.Row();

        var dto = PanelTaskMapper.From(task, "Taking a backup", includeLog: true);

        Assert.True(dto.LogIncluded);
        Assert.Equal(string.Empty, dto.Log);
    }
}
