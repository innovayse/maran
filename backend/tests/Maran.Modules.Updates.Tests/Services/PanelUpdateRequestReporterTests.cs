using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.ValueObjects;
using Maran.Modules.Updates.Services;
using Maran.Modules.Updates.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Updates.Tests.Services;

/// <summary>How the root update's reports move the task of a requested update and close it.</summary>
public sealed class PanelUpdateRequestReporterTests
{
    /// <summary>Everything a reporter needs over one isolated server whose ledger names an open request.</summary>
    private sealed class Rig : IDisposable
    {
        /// <summary>The isolated server.</summary>
        public UpdatesWorld World { get; }

        /// <summary>The journal's writer.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The task recorder.</summary>
        public RecordingTaskRecorder Tasks { get; }

        /// <summary>The reporter under test.</summary>
        public PanelUpdateRequestReporter Reporter { get; }

        /// <summary>Builds the reporter over a fresh world.</summary>
        /// <param name="openRequest">Whether the ledger names a task.</param>
        public Rig(bool openRequest = true)
        {
            World = new UpdatesWorld();
            Audit = new RecordingAuditWriter();
            Tasks = new RecordingTaskRecorder();
            Reporter = new PanelUpdateRequestReporter(Tasks, World.Ledger, new UpdatesAuditJournal(Audit, new FakeCurrentUser()));
            if (openRequest)
            {
                World.Ledger.TryWrite(Tasks.NextTaskId);
            }
        }

        /// <summary>Builds a report that is known to be valid.</summary>
        /// <param name="word">The word.</param>
        /// <param name="to">The second release name, or null.</param>
        /// <returns>The report.</returns>
        public static UpdateRequestReport Report(PanelUpdateRunWord word, string? to = "1.1.0")
        {
            Assert.True(UpdateRequestReport.TryCreate(word, "1.0.0", to, out var report));
            return report!;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            World.Dispose();
        }
    }

    /// <summary>The installing report advances the task and leaves the ledger and the journal alone.</summary>
    [Fact]
    public async Task The_installing_report_advances_the_task_and_closes_nothing()
    {
        using var rig = new Rig();

        await rig.Reporter.ReportAsync(Rig.Report(PanelUpdateRunWord.Installing), CancellationToken.None);

        Assert.Single(rig.Tasks.Calls);
        Assert.Contains("Installing 1.1.0 over 1.0.0.", rig.Tasks.Calls[0], StringComparison.Ordinal);
        Assert.Equal(rig.Tasks.NextTaskId, rig.World.Ledger.TryRead());
        Assert.Empty(rig.Audit.Entries);
    }

    /// <summary>An applied update completes the task, removes the ledger and journals a success by the panel itself.</summary>
    [Fact]
    public async Task An_applied_update_completes_the_task_and_is_journalled_as_a_success()
    {
        using var rig = new Rig();

        await rig.Reporter.ReportAsync(Rig.Report(PanelUpdateRunWord.Applied), CancellationToken.None);

        Assert.Contains($"complete {rig.Tasks.NextTaskId}", rig.Tasks.Calls);
        Assert.DoesNotContain(rig.Tasks.Calls, call =>
        {
            return call.StartsWith("fail", StringComparison.Ordinal);
        });
        Assert.Null(rig.World.Ledger.TryRead());
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(UpdatesAuditActions.PanelUpdateRunApplied, entry.Action);
        Assert.Equal("from=1.0.0;to=1.1.0", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("maran-updates", entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
    }

    /// <summary>Already being on the newest release completes the task and names no second release in the journal.</summary>
    [Fact]
    public async Task An_update_that_found_nothing_newer_completes_the_task()
    {
        using var rig = new Rig();

        await rig.Reporter.ReportAsync(Rig.Report(PanelUpdateRunWord.Unchanged, to: null), CancellationToken.None);

        Assert.Contains($"complete {rig.Tasks.NextTaskId}", rig.Tasks.Calls);
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(UpdatesAuditActions.PanelUpdateRunUnchanged, entry.Action);
        Assert.Equal("from=1.0.0", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Each failing word fails the task under its own code, removes the ledger and journals a failure.</summary>
    [Theory]
    [InlineData(PanelUpdateRunWord.Refused, "PanelUpdateRefused", "PanelUpdateRunRefused")]
    [InlineData(PanelUpdateRunWord.RolledBack, "PanelUpdateRolledBack", "PanelUpdateRunRolledBack")]
    [InlineData(PanelUpdateRunWord.RollbackFailed, "PanelUpdateRollbackFailed", "PanelUpdateRunRollbackFailed")]
    [InlineData(PanelUpdateRunWord.Interrupted, "PanelUpdateInterrupted", "PanelUpdateRunInterrupted")]
    public async Task A_failing_word_fails_the_task_under_its_own_code(PanelUpdateRunWord word, string code, string action)
    {
        using var rig = new Rig();

        await rig.Reporter.ReportAsync(Rig.Report(word, word == PanelUpdateRunWord.Refused ? null : "1.1.0"), CancellationToken.None);

        Assert.Contains($"fail {rig.Tasks.NextTaskId} {code}", rig.Tasks.Calls);
        Assert.DoesNotContain($"complete {rig.Tasks.NextTaskId}", rig.Tasks.Calls);
        Assert.Null(rig.World.Ledger.TryRead());
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(action, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A rolled-back update tells the administrator the health check failed, the database was not restored, and where the dump is.</summary>
    [Fact]
    public async Task A_rolled_back_update_says_the_database_was_not_restored_and_where_the_dump_is()
    {
        using var rig = new Rig();

        await rig.Reporter.ReportAsync(Rig.Report(PanelUpdateRunWord.RolledBack), CancellationToken.None);

        var log = Assert.Single(rig.Tasks.Calls, call =>
        {
            return call.StartsWith("report", StringComparison.Ordinal);
        });
        Assert.Contains("health check", log, StringComparison.Ordinal);
        Assert.Contains("NOT restored", log, StringComparison.Ordinal);
        Assert.Contains("/var/backups/maran-update", log, StringComparison.Ordinal);
    }

    /// <summary>A report with no open request touches no task but is still journalled.</summary>
    [Fact]
    public async Task A_report_with_no_open_request_touches_no_task_but_is_still_journalled()
    {
        using var rig = new Rig(openRequest: false);

        await rig.Reporter.ReportAsync(Rig.Report(PanelUpdateRunWord.RolledBack), CancellationToken.None);

        Assert.Empty(rig.Tasks.Calls);
        Assert.Single(rig.Audit.Entries);
    }

    /// <summary>A garbled ledger is the same as none: no task is touched and the damaged file is removed.</summary>
    [Fact]
    public async Task A_garbled_ledger_touches_no_task_and_is_removed()
    {
        using var rig = new Rig(openRequest: false);
        File.WriteAllText(rig.World.Options.TaskLedgerPath, "not a task id");

        await rig.Reporter.ReportAsync(Rig.Report(PanelUpdateRunWord.Applied), CancellationToken.None);

        Assert.Empty(rig.Tasks.Calls);
        Assert.False(File.Exists(rig.World.Options.TaskLedgerPath));
    }
}
