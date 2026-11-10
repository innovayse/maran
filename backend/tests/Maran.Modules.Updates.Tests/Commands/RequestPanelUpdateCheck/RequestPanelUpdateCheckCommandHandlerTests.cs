using Maran.Modules.Updates.Commands.RequestPanelUpdateCheck;
using Maran.Modules.Updates.Services;
using Maran.Modules.Updates.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Updates.Tests.Commands.RequestPanelUpdateCheck;

/// <summary>
/// Requesting a release check from the panel (issue #516): what a request creates, in which order, and
/// everything it refuses. The resource is server-wide, so the tenant IDOR test does not apply to it.
/// </summary>
public sealed class RequestPanelUpdateCheckCommandHandlerTests
{
    /// <summary>The members of the request, none of which a caller may set.</summary>
    private static readonly string[] CallerControlledMembers = ["IpAddress", "UserAgent"];

    /// <summary>The request the controller builds: only server-established members.</summary>
    private static readonly RequestPanelUpdateCheckCommand Request = new("203.0.113.9", "test-agent");

    /// <summary>Everything a handler needs over one isolated server.</summary>
    private sealed class Rig : IDisposable
    {
        /// <summary>The isolated server.</summary>
        public UpdatesWorld World { get; }

        /// <summary>The journal's writer.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The handler under test.</summary>
        public RequestPanelUpdateCheckCommandHandler Handler { get; }

        /// <summary>Builds the handler over a fresh world.</summary>
        /// <param name="createRequestDirectory">Whether the installer's request directory exists.</param>
        public Rig(bool createRequestDirectory = true)
        {
            World = new UpdatesWorld(createRequestDirectory);
            Audit = new RecordingAuditWriter(() =>
            {
                return World.CheckMarkerExists();
            });
            Handler = new RequestPanelUpdateCheckCommandHandler(
                World.Reader,
                World.CheckMarker,
                new UpdatesAuditJournal(Audit, new FakeCurrentUser()));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            World.Dispose();
        }
    }

    /// <summary>A request creates the fixed check marker, and not the update marker.</summary>
    [Fact]
    public async Task A_request_creates_the_check_marker_and_not_the_update_marker()
    {
        using var rig = new Rig();

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("check\n", File.ReadAllText(rig.World.Options.CheckMarkerPath));
        Assert.False(rig.World.MarkerExists());
    }

    /// <summary>The audit entry names the administrator and the request's origin, and is written before the marker exists.</summary>
    [Fact]
    public async Task The_audit_entry_is_written_before_the_marker_exists()
    {
        using var rig = new Rig();

        await rig.Handler.HandleAsync(Request, CancellationToken.None);

        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(UpdatesAuditActions.PanelUpdateCheckRequested, entry.Action);
        Assert.Equal("request=check", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(FakeCurrentUser.AdministratorId, entry.ActorUserId);
        Assert.Equal("203.0.113.9", entry.IpAddress);
        Assert.Equal("test-agent", entry.UserAgent);
        Assert.False(Assert.Single(rig.Audit.Observations), "the marker already existed when the audit entry was written");
        Assert.True(rig.World.CheckMarkerExists());
    }

    /// <summary>A request while a check is queued is refused as a conflict and leaves the marker alone.</summary>
    [Fact]
    public async Task A_request_while_a_check_is_queued_is_refused()
    {
        using var rig = new Rig();
        rig.World.PlantCheckMarker(TimeSpan.FromMinutes(1));

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PanelUpdateCheckRunning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.False(Assert.Single(rig.Audit.Entries).Succeeded);
    }

    /// <summary>A request while an update is queued or running is refused with the update's own code, and creates nothing.</summary>
    [Fact]
    public async Task A_request_while_an_update_is_queued_is_refused()
    {
        using var rig = new Rig();
        rig.World.PlantMarker(TimeSpan.FromMinutes(1));

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateRunning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.False(rig.World.CheckMarkerExists());
        Assert.False(Assert.Single(rig.Audit.Entries).Succeeded);
    }

    /// <summary>A check marker the root unit never took is withdrawn and replaced, so one lost request never blocks the button for good.</summary>
    [Fact]
    public async Task A_check_marker_nobody_picked_up_is_replaced()
    {
        using var rig = new Rig();
        rig.World.PlantCheckMarker(TimeSpan.FromHours(1));
        var plantedAt = rig.World.Clock.UtcNow.UtcDateTime - TimeSpan.FromHours(1);

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(rig.World.CheckMarkerExists());
        Assert.NotEqual(plantedAt, File.GetLastWriteTimeUtc(rig.World.Options.CheckMarkerPath));
    }

    /// <summary>On a server whose installer did not build the request directory the request is refused as unavailable and says nothing about a path.</summary>
    [Fact]
    public async Task Without_the_request_directory_the_request_is_unavailable()
    {
        using var rig = new Rig(createRequestDirectory: false);

        var result = await rig.Handler.HandleAsync(Request, CancellationToken.None);

        Assert.Equal("PanelUpdateUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.False(Assert.Single(rig.Audit.Entries).Succeeded);
    }

    /// <summary>Every member of the command is server-established: closed against the body and against the query string, and nothing else is accepted.</summary>
    [Fact]
    public void The_command_has_only_server_established_members()
    {
        var members = typeof(RequestPanelUpdateCheckCommand).GetConstructors().Single().GetParameters();

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
            typeof(RequestPanelUpdateCheckCommand).GetProperties().Where(property =>
            {
                return property.Name != "EqualityContract";
            }),
            property =>
            {
                Assert.NotNull(property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false).SingleOrDefault());
            });
    }
}
