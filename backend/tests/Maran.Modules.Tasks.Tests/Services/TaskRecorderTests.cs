using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// The recorder's contract: it writes what an instrumented operation reports, and it never throws
/// into that operation whatever happens to its own writes.
/// </summary>
public sealed class TaskRecorderTests
{
    /// <summary>Beginning a task records its kind subject and correlation id as running.</summary>
    [Fact]
    public async Task Beginning_a_task_records_its_kind_subject_and_correlation_id_as_running()
    {
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var recorder = Recorder(context);

        var id = await recorder.BeginAsync(TaskKinds.AccountDeletion, "alice", "corr-1", CancellationToken.None);

        var stored = await Read(database, id);
        Assert.Equal(TaskKinds.AccountDeletion, stored.Kind);
        Assert.Equal("alice", stored.Subject);
        Assert.Equal("corr-1", stored.CorrelationId);
        Assert.Equal(PanelTaskStatus.Running, stored.Status);
        Assert.Equal(TasksTestContext.Now, stored.StartedAt);
    }

    /// <summary>Reporting progress stores the percentage and appends the line.</summary>
    [Fact]
    public async Task Reporting_progress_stores_the_percentage_and_appends_the_line()
    {
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var recorder = Recorder(context);
        var id = await recorder.BeginAsync(TaskKinds.CertificateIssue, "example.com", null, CancellationToken.None);

        await recorder.ReportAsync(id, 40, "ordering the certificate", CancellationToken.None);

        var stored = await Read(database, id);
        Assert.Equal(40, stored.Percent);
        Assert.Equal("ordering the certificate", stored.Log);
    }

    /// <summary>Completing a task closes it and failing it afterwards changes nothing.</summary>
    [Fact]
    public async Task Completing_a_task_closes_it_and_failing_it_afterwards_changes_nothing()
    {
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var recorder = Recorder(context);
        var id = await recorder.BeginAsync(TaskKinds.CertificateIssue, "example.com", null, CancellationToken.None);

        await recorder.CompleteAsync(id, CancellationToken.None);
        await recorder.FailAsync(id, "SiteNotFound", CancellationToken.None);

        var stored = await Read(database, id);
        Assert.Equal(PanelTaskStatus.Completed, stored.Status);
        Assert.Null(stored.ErrorCode);
    }

    /// <summary>Failing a task records the code the operation answered with.</summary>
    [Fact]
    public async Task Failing_a_task_records_the_code_the_operation_answered_with()
    {
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var recorder = Recorder(context);
        var id = await recorder.BeginAsync(TaskKinds.CertificateIssue, "example.com", null, CancellationToken.None);

        await recorder.FailAsync(id, "AcmeAuthorityUnreachable", CancellationToken.None);

        var stored = await Read(database, id);
        Assert.Equal(PanelTaskStatus.Failed, stored.Status);
        Assert.Equal("AcmeAuthorityUnreachable", stored.ErrorCode);
    }

    /// <summary>A recorder that cannot open a task never throws into the operation it records.</summary>
    /// <remarks>
    /// The first half of the one property this class exists for. The caller here stands for an
    /// account deletion about to start: a tasks database that has gone away must cost it a progress
    /// bar and nothing else.
    /// </remarks>
    [Fact]
    public async Task A_recorder_that_cannot_open_a_task_never_throws_into_the_operation_it_records()
    {
        await using var context = TasksTestContext.Create(
            FakeCurrentUser.Admin(), saveFailure: new InvalidOperationException("the tasks database is gone"));
        var recorder = Recorder(context);

        var id = await recorder.BeginAsync(TaskKinds.AccountDeletion, "alice", null, CancellationToken.None);

        // Reaching this line at all is the assertion; the id is asserted so the test also pins the
        // contract that a failed begin answers "there is no task" rather than a usable id.
        Assert.Equal(Guid.Empty, id);
    }

    /// <summary>A recorder whose writes fail after a task is open never throws into the operation.</summary>
    /// <remarks>
    /// The second half, and the one a one-shot fixture cannot reach: with the failure armed only
    /// after the opening save, the task IS open, so the report, the completion and the failure each
    /// run their real database write and each throw. A wrap that covers the begin and three of the
    /// four mutations is a wrap that takes the panel down on the fourth, mid-deletion.
    /// </remarks>
    [Fact]
    public async Task A_recorder_whose_writes_fail_after_a_task_is_open_never_throws_into_the_operation()
    {
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(
            FakeCurrentUser.Admin(),
            database,
            saveFailure: new InvalidOperationException("the tasks database is gone"),
            savesBeforeFailure: 1);
        var recorder = Recorder(context);
        var id = await recorder.BeginAsync(TaskKinds.AccountDeletion, "alice", null, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, id);

        await recorder.ReportAsync(id, 50, "cascading", CancellationToken.None);
        await recorder.CompleteAsync(id, CancellationToken.None);
        await recorder.FailAsync(id, "AccountCleanupFailed", CancellationToken.None);

        // None of the three reached the table, which is what "the pane loses an update" looks like —
        // and the operation that reported them is none the wiser.
        var stored = await Read(database, id);
        Assert.Equal(PanelTaskStatus.Running, stored.Status);
        Assert.Equal(0, stored.Revision);
    }

    /// <summary>A recorder handed a cancelled token never throws into the operation it records.</summary>
    /// <remarks>
    /// Cancellation is swallowed with everything else, and this is where that is pinned. Rethrowing
    /// it would be the recorder throwing into its caller on the one path where the caller is least
    /// able to cope: mid-shutdown, holding a half-finished cascade. The caller's own token is what
    /// stops the caller.
    /// </remarks>
    [Fact]
    public async Task A_recorder_handed_a_cancelled_token_never_throws_into_the_operation_it_records()
    {
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var recorder = Recorder(context);
        var open = await recorder.BeginAsync(TaskKinds.AccountDeletion, "alice", null, CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var thrown = await Record.ExceptionAsync(async () =>
        {
            await recorder.ReportAsync(open, 50, "cascading", cancelled.Token);
            await recorder.CompleteAsync(open, cancelled.Token);
            await recorder.FailAsync(open, "AccountCleanupFailed", cancelled.Token);
        });

        Assert.Null(thrown);

        // And the cancellation was genuinely observed rather than ignored by the provider: the row
        // is untouched, which is what makes the three swallowed cancellations real ones.
        var stored = await Read(database, open);
        Assert.Equal(PanelTaskStatus.Running, stored.Status);
        Assert.Equal(0, stored.Revision);
    }

    /// <summary>A report against a task that was never opened is a no-op rather than a failure.</summary>
    [Fact]
    public async Task A_report_against_a_task_that_was_never_opened_is_a_no_op_rather_than_a_failure()
    {
        // The other half of the empty-id contract: an instrumented operation passes whatever begin
        // answered straight back in, with no branch of its own, so the empty id has to be safe.
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var recorder = Recorder(context);

        await recorder.ReportAsync(Guid.Empty, 50, "cascading", CancellationToken.None);
        await recorder.CompleteAsync(Guid.Empty, CancellationToken.None);
        await recorder.FailAsync(Guid.Empty, "AccountCleanupFailed", CancellationToken.None);

        await using var reader = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await reader.PanelTasks.ToListAsync());
    }

    /// <summary>A report against an unknown task is a no-op rather than a failure.</summary>
    [Fact]
    public async Task A_report_against_an_unknown_task_is_a_no_op_rather_than_a_failure()
    {
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());
        var recorder = Recorder(context);

        await recorder.ReportAsync(Guid.NewGuid(), 50, "cascading", CancellationToken.None);
    }

    /// <summary>A recorder records for a customer principal as readily as for an administrator.</summary>
    [Fact]
    public async Task A_recorder_records_for_a_customer_principal_as_readily_as_for_an_administrator()
    {
        // The recorder runs inside somebody else's handler, and for the unattended renewal pass
        // inside no request at all. If its own reads honoured the module's administrator filter,
        // every stage after the first would silently find no row and the task would sit at zero.
        var database = Guid.NewGuid().ToString();
        await using var context = TasksTestContext.Create(FakeCurrentUser.Customer(), database);
        var recorder = Recorder(context);

        var id = await recorder.BeginAsync(TaskKinds.CertificateIssue, "example.com", null, CancellationToken.None);
        await recorder.ReportAsync(id, 60, "installing", CancellationToken.None);
        await recorder.CompleteAsync(id, CancellationToken.None);

        var stored = await Read(database, id);
        Assert.Equal(PanelTaskStatus.Completed, stored.Status);
        Assert.Equal("installing", stored.Log);
    }

    /// <summary>Builds the recorder under test over a context.</summary>
    /// <param name="context">The context it writes through.</param>
    /// <returns>The recorder.</returns>
    private static TaskRecorder Recorder(TasksDbContext context)
    {
        return new TaskRecorder(context, new FakeClock(TasksTestContext.Now), NullLogger<TaskRecorder>.Instance);
    }

    /// <summary>Reads one task back through a fresh context, as a later request would.</summary>
    /// <param name="database">The in-memory database holding it.</param>
    /// <param name="id">The task to read.</param>
    /// <returns>The stored row.</returns>
    private static async Task<PanelTask> Read(string database, Guid id)
    {
        await using var reader = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        return await reader.PanelTasks.SingleAsync(task => task.Id == id);
    }
}
