using Maran.Modules.Updates.Commands.StartPanelUpdate;
using Maran.Modules.Updates.Services;
using Maran.Modules.Updates.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Updates.Tests.Commands.StartPanelUpdate;

/// <summary>
/// Requesting the panel update (issue #382): what a request creates, in which order, and everything it
/// refuses. The resource is server-wide, so the tenant IDOR test does not apply to it.
/// </summary>
public sealed class StartPanelUpdateCommandHandlerTests
{
    /// <summary>The members of the request, none of which a caller may set.</summary>
    private static readonly string[] CallerControlledMembers = ["IpAddress", "UserAgent", "CorrelationId"];

    /// <summary>The request the controller builds: only server-established members.</summary>
    private static readonly StartPanelUpdateCommand Request = new("203.0.113.9", "test-agent", "corr-1");

    /// <summary>Everything a handler needs over one isolated server.</summary>
    private sealed class Rig : IDisposable
    {
        /// <summary>The isolated server.</summary>
        public UpdatesWorld World { get; }

        /// <summary>The journal's writer.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The task recorder.</summary>
        public RecordingTaskRecorder Tasks { get; }

        /// <summary>The handler under test.</summary>
        public StartPanelUpdateCommandHandler Handler { get; }

        /// <summary>Builds the handler over a fresh world.</summary>
        /// <param name="createRequestDirectory">Whether the installer's request directory exists.</param>
        public Rig(bool createRequestDirectory = true)
        {
            World = new UpdatesWorld(createRequestDirectory);
            Audit = new RecordingAuditWriter(() =>
            {
                return World.MarkerExists();
            });
            Tasks = new RecordingTaskRecorder();
            Handler = new StartPanelUpdateCommandHandler(
                World.Reader,
                Tasks,
                World.Ledger,
                World.Marker,
                new UpdatesAuditJournal(Audit, new FakeCurrentUser()));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            World.Dispose();
        }
    }

    /// <summary>A request creates the task, the ledger and the marker, and answers with the task's id.</summary>
    [Fact]
    public async Task A_request_creates_the_task_the_ledger_and_the_marker_and_answers_with_the_task()
    {
        using var rig = new Rig();

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(rig.Tasks.NextTaskId, result.Value.TaskId);
        Assert.Equal(TaskKinds.PanelUpdate, rig.Tasks.BegunKind);
        Assert.Equal(TaskAudience.AdministratorOnly, rig.Tasks.BegunAudience);
        Assert.Null(rig.Tasks.BegunAccountId);
        Assert.Equal(rig.Tasks.NextTaskId, rig.World.Ledger.TryRead());
        Assert.Equal("update\n", File.ReadAllText(rig.World.Options.MarkerPath));
    }

    /// <summary>The audit entry names the administrator and the request's origin, and is written before the marker exists.</summary>
    [Fact]
    public async Task The_audit_entry_is_written_before_the_marker_exists()
    {
        using var rig = new Rig();

        await rig.Handler.HandleAsync(Request, CancellationToken.None);

        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(UpdatesAuditActions.PanelUpdateRequested, entry.Action);
        Assert.Equal("request=update", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(FakeCurrentUser.AdministratorId, entry.ActorUserId);
        Assert.Equal("203.0.113.9", entry.IpAddress);
        Assert.Equal("test-agent", entry.UserAgent);
        Assert.False(Assert.Single(rig.Audit.Observations), "the marker already existed when the audit entry was written");
        Assert.True(rig.World.MarkerExists());
    }

    /// <summary>A request while an update is queued or running is refused as a conflict and opens no second task.</summary>
    [Fact]
    public async Task A_request_while_an_update_is_queued_is_refused_and_opens_no_second_task()
    {
        using var rig = new Rig();
        rig.World.PlantMarker(TimeSpan.FromMinutes(1));

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PanelUpdateRunning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(rig.Tasks.Calls);
        Assert.Null(rig.World.Ledger.TryRead());
        Assert.False(Assert.Single(rig.Audit.Entries).Succeeded);
    }

    /// <summary>A request while root says an update holds the lock is refused the same way.</summary>
    [Fact]
    public async Task A_request_while_root_is_running_an_update_is_refused()
    {
        using var rig = new Rig();
        rig.World.WriteRunningState($"STARTED_AT={rig.World.Instant(TimeSpan.FromMinutes(3))}\n");

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateRunning", result.Error!.Code);
        Assert.False(rig.World.MarkerExists());
    }

    /// <summary>A stale marker no longer counts as an update in flight, but the exclusive creation still refuses to overwrite it until the reaper withdraws it.</summary>
    [Fact]
    public async Task A_stale_marker_is_not_overwritten_and_the_request_fails_its_task()
    {
        using var rig = new Rig();
        rig.World.PlantMarker(TimeSpan.FromMinutes(30));

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateRunning", result.Error!.Code);
        Assert.Contains($"fail {rig.Tasks.NextTaskId} PanelUpdateRunning", rig.Tasks.Calls);
        Assert.Null(rig.World.Ledger.TryRead());
    }

    /// <summary>A server that cannot hand a request to root is refused as unavailable and creates nothing.</summary>
    [Fact]
    public async Task A_server_that_cannot_hand_the_request_to_root_is_refused_as_unavailable()
    {
        using var rig = new Rig(createRequestDirectory: false);

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Empty(rig.Tasks.Calls);
        Assert.Null(rig.World.Ledger.TryRead());
    }

    /// <summary>A recorder that could not open a task refuses the request before anything is created.</summary>
    [Fact]
    public async Task A_task_that_could_not_be_opened_refuses_the_request_before_anything_is_created()
    {
        using var rig = new Rig();
        rig.Tasks.NextTaskId = Guid.Empty;

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateUnavailable", result.Error!.Code);
        Assert.False(rig.World.MarkerExists());
        Assert.Null(rig.World.Ledger.TryRead());
    }

    /// <summary>The race: when another request created the marker between the check and the creation, this one fails its task and takes back its ledger.</summary>
    [Fact]
    public async Task A_marker_created_in_the_meantime_fails_the_task_and_removes_the_ledger()
    {
        using var rig = new Rig();
        var audit = new RecordingAuditWriter(() =>
        {
            // The rival request creates the marker while this one is journalling, which is after
            // the in-progress check has passed and before this request creates its own marker.
            File.WriteAllText(rig.World.Options.MarkerPath, "update\n");
            return false;
        });
        var handler = new StartPanelUpdateCommandHandler(
            rig.World.Reader,
            rig.Tasks,
            rig.World.Ledger,
            rig.World.Marker,
            new UpdatesAuditJournal(audit, new FakeCurrentUser()));

        var result = await handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateRunning", result.Error!.Code);
        Assert.Contains($"fail {rig.Tasks.NextTaskId} PanelUpdateRunning", rig.Tasks.Calls);
        Assert.Null(rig.World.Ledger.TryRead());
        Assert.Equal(2, audit.Entries.Count);
        Assert.True(audit.Entries[0].Succeeded);
        Assert.False(audit.Entries[1].Succeeded);
    }

    /// <summary>A ledger that cannot be written fails the task and creates no marker, so root never starts an update the panel cannot follow.</summary>
    [Fact]
    public async Task A_ledger_that_cannot_be_written_creates_no_marker()
    {
        using var rig = new Rig();
        rig.World.Options.TaskLedgerPath = Path.Combine(rig.World.Options.RequestDirectory, "missing", "ledger");

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateUnavailable", result.Error!.Code);
        Assert.False(rig.World.MarkerExists());
        Assert.Contains($"fail {rig.Tasks.NextTaskId} PanelUpdateUnavailable", rig.Tasks.Calls);
    }

    /// <summary>A ledger another request left open is refused and kept: the loser fails only its own task and creates no marker.</summary>
    [Fact]
    public async Task A_ledger_another_request_left_open_is_kept_and_this_request_fails_only_its_own_task()
    {
        using var rig = new Rig();
        var other = Guid.NewGuid();
        rig.World.Ledger.TryWrite(other);

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateRunning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Contains($"fail {rig.Tasks.NextTaskId} PanelUpdateRunning", rig.Tasks.Calls);
        Assert.Equal(other, rig.World.Ledger.TryRead());
        Assert.False(rig.World.MarkerExists());
    }

    /// <summary>The request carries nothing a caller chose: the command has only server-established members, each guarded against binding.</summary>
    [Fact]
    public void The_request_carries_no_caller_controlled_value()
    {
        var members = typeof(StartPanelUpdateCommand)
            .GetConstructors()
            .Single()
            .GetParameters();

        Assert.Equal(
            CallerControlledMembers,
            members.Select(member =>
            {
                return member.Name!;
            }).ToArray());
        Assert.All(members, member =>
        {
            Assert.NotNull(member.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), false).SingleOrDefault());
        });
        Assert.All(
            typeof(StartPanelUpdateCommand).GetProperties().Where(property =>
            {
                return property.Name != "EqualityContract";
            }),
            property =>
            {
                Assert.NotNull(property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false).SingleOrDefault());
            });
    }
}
