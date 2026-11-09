using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Services;
using Maran.Modules.Updates.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Updates.Tests.Services;

/// <summary>The pass that closes the task of a requested update whose report never arrived.</summary>
public sealed class PanelUpdateTaskReaperTests
{
    /// <summary>Everything a reaper needs over one isolated server whose ledger names an open request.</summary>
    private sealed class Rig : IDisposable
    {
        /// <summary>The container the reaper's scopes come from.</summary>
        private readonly ServiceProvider _provider;

        /// <summary>The isolated server.</summary>
        public UpdatesWorld World { get; }

        /// <summary>The journal's writer.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The task recorder.</summary>
        public RecordingTaskRecorder Tasks { get; }

        /// <summary>The reaper under test.</summary>
        public PanelUpdateTaskReaper Reaper { get; }

        /// <summary>Builds the reaper over a fresh world with an open request.</summary>
        public Rig()
        {
            World = new UpdatesWorld();
            Audit = new RecordingAuditWriter();
            Tasks = new RecordingTaskRecorder();
            _provider = new ServiceCollection()
                .AddSingleton<ITaskRecorder>(Tasks)
                .AddSingleton<IAuditWriter>(Audit)
                .AddSingleton<ICurrentUser, FakeCurrentUser>()
                .AddScoped<UpdatesAuditJournal>()
                .BuildServiceProvider();
            Reaper = new PanelUpdateTaskReaper(
                World.Reader,
                World.Ledger,
                World.Marker,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<PanelUpdateTaskReaper>.Instance);
            World.Ledger.TryWrite(Tasks.NextTaskId);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
            World.Dispose();
        }
    }

    /// <summary>A request that was just made is left alone, marker and all.</summary>
    [Fact]
    public async Task A_request_that_was_just_made_is_left_alone()
    {
        using var rig = new Rig();
        rig.World.PlantMarker(TimeSpan.FromSeconds(20));

        Assert.Equal(PanelUpdateLedgerVerdict.Leave, await rig.Reaper.ReapAsync(CancellationToken.None));

        Assert.Empty(rig.Tasks.Calls);
        Assert.True(rig.World.MarkerExists());
        Assert.NotNull(rig.World.Ledger.TryRead());
    }

    /// <summary>A run that root says is running is left alone however old the ledger is.</summary>
    [Fact]
    public async Task A_run_that_root_says_is_running_is_left_alone()
    {
        using var rig = new Rig();
        rig.World.AgeLedger(TimeSpan.FromHours(1));
        rig.World.WriteRunningState($"STARTED_AT={rig.World.Instant(TimeSpan.FromMinutes(40))}\n");

        Assert.Equal(PanelUpdateLedgerVerdict.Leave, await rig.Reaper.ReapAsync(CancellationToken.None));

        Assert.Empty(rig.Tasks.Calls);
    }

    /// <summary>A marker root never picked up is withdrawn, the task fails as never started, and the next request is no longer refused.</summary>
    [Fact]
    public async Task A_marker_root_never_picked_up_is_withdrawn_and_the_task_fails_as_never_started()
    {
        using var rig = new Rig();
        rig.World.PlantMarker(TimeSpan.FromMinutes(10));

        Assert.Equal(PanelUpdateLedgerVerdict.NeverStarted, await rig.Reaper.ReapAsync(CancellationToken.None));

        Assert.False(rig.World.MarkerExists());
        Assert.Null(rig.World.Ledger.TryRead());
        Assert.Contains($"fail {rig.Tasks.NextTaskId} PanelUpdateNotStarted", rig.Tasks.Calls);
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(UpdatesAuditActions.PanelUpdateRunNotStarted, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A ledger with no marker and nothing running that has outlived its grace period fails the task as of unknown outcome.</summary>
    [Fact]
    public async Task An_old_ledger_with_no_marker_and_nothing_running_fails_the_task_as_unknown()
    {
        using var rig = new Rig();
        rig.World.AgeLedger(TimeSpan.FromMinutes(10));

        Assert.Equal(PanelUpdateLedgerVerdict.OutcomeUnknown, await rig.Reaper.ReapAsync(CancellationToken.None));

        Assert.Contains($"fail {rig.Tasks.NextTaskId} PanelUpdateOutcomeUnknown", rig.Tasks.Calls);
        Assert.Null(rig.World.Ledger.TryRead());
        Assert.Equal(UpdatesAuditActions.PanelUpdateRunOutcomeUnknown, Assert.Single(rig.Audit.Entries).Action);
    }

    /// <summary>A young ledger with no marker is not judged yet: the report may still be on its way.</summary>
    [Fact]
    public async Task A_young_ledger_with_no_marker_is_not_judged_yet()
    {
        using var rig = new Rig();

        Assert.Equal(PanelUpdateLedgerVerdict.Leave, await rig.Reaper.ReapAsync(CancellationToken.None));

        Assert.Empty(rig.Tasks.Calls);
    }

    /// <summary>With no ledger there is nothing to reap, even beside an old marker.</summary>
    [Fact]
    public async Task With_no_ledger_there_is_nothing_to_reap()
    {
        using var rig = new Rig();
        rig.World.Ledger.Remove();
        rig.World.PlantMarker(TimeSpan.FromMinutes(30));

        Assert.Equal(PanelUpdateLedgerVerdict.Leave, await rig.Reaper.ReapAsync(CancellationToken.None));

        Assert.Empty(rig.Tasks.Calls);
    }
}
