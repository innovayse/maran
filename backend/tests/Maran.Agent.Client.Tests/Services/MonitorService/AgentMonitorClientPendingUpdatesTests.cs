using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.MonitorService;

/// <summary>
/// Mapping contract of <see cref="AgentMonitorClient.ListPendingUpdatesAsync"/> (issue #324): rows reach the
/// panel with an unreported installed version read once as null, and a catalogue the agent could not read is
/// a failure, never an empty list.
/// </summary>
public sealed class AgentMonitorClientPendingUpdatesTests
{
    /// <summary>Every wire row reaches the panel in the order the agent sent it, with all four fields.</summary>
    [Fact]
    public async Task Every_wire_row_reaches_the_panel_in_order_with_all_its_fields()
    {
        var stub = new StubMonitorService
        {
            PendingUpdatesResponse = new ListPendingUpdatesResponse
            {
                Ok = new ListPendingUpdatesOk
                {
                    Updates =
                    {
                        Row("libssl3t64", "3.0.13-0ubuntu3.5", "3.0.13-0ubuntu3.4", "noble-updates"),
                        Row("g++", "4:13.2.0-7ubuntu1", "4:13.2.0-1ubuntu1", "noble"),
                    },
                },
            },
        };

        var result = await Client(stub).ListPendingUpdatesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                new AgentPendingUpdate("libssl3t64", "3.0.13-0ubuntu3.5", "3.0.13-0ubuntu3.4", "noble-updates"),
                new AgentPendingUpdate("g++", "4:13.2.0-7ubuntu1", "4:13.2.0-1ubuntu1", "noble"),
            ],
            result.Value);
        Assert.NotNull(stub.LastPendingUpdatesRequest);
    }

    /// <summary>An empty installed version on the wire is null in the panel, never an empty string.</summary>
    [Fact]
    public async Task An_empty_installed_version_on_the_wire_becomes_null_in_the_panel()
    {
        var stub = new StubMonitorService
        {
            PendingUpdatesResponse = new ListPendingUpdatesResponse
            {
                Ok = new ListPendingUpdatesOk { Updates = { Row("openssl", "3.0.7-27.el9", string.Empty, "baseos") } },
            },
        };

        var result = await Client(stub).ListPendingUpdatesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!);
        Assert.Null(row.InstalledVersion);
        Assert.Equal("3.0.7-27.el9", row.AvailableVersion);
    }

    /// <summary>An absent source stays an empty string, so a screen never prints a missing value as a name.</summary>
    [Fact]
    public async Task An_absent_source_stays_an_empty_string()
    {
        var stub = new StubMonitorService
        {
            PendingUpdatesResponse = new ListPendingUpdatesResponse
            {
                Ok = new ListPendingUpdatesOk { Updates = { Row("tzdata", "2024b-0ubuntu0.24.04", "2024a", string.Empty) } },
            },
        };

        var result = await Client(stub).ListPendingUpdatesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, Assert.Single(result.Value!).Source);
    }

    /// <summary>A catalogue with nothing upgradable is a successful, empty finding.</summary>
    [Fact]
    public async Task A_catalogue_with_nothing_upgradable_is_a_successful_empty_list()
    {
        var stub = new StubMonitorService
        {
            PendingUpdatesResponse = new ListPendingUpdatesResponse { Ok = new ListPendingUpdatesOk() },
        };

        var result = await Client(stub).ListPendingUpdatesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    /// <summary>A catalogue the agent could not read is a failure carrying the agent code, never an empty list.</summary>
    [Fact]
    public async Task An_unreadable_catalogue_is_a_failure_carrying_the_agent_code_never_an_empty_list()
    {
        var stub = new StubMonitorService
        {
            PendingUpdatesResponse = new ListPendingUpdatesResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "cache-only enabled but no cache" },
            },
        };

        var result = await Client(stub).ListPendingUpdatesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A response with neither branch set is refused rather than read as no updates.</summary>
    [Fact]
    public async Task A_response_with_neither_branch_set_is_refused_rather_than_read_as_no_updates()
    {
        var result = await Client(new StubMonitorService()).ListPendingUpdatesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds one wire row.</summary>
    /// <param name="name">The package name.</param>
    /// <param name="available">The version the update installs.</param>
    /// <param name="installed">The installed version; empty when not reported.</param>
    /// <param name="source">The origin or repository id.</param>
    /// <returns>The wire row.</returns>
    private static PendingUpdate Row(string name, string available, string installed, string source)
    {
        return new PendingUpdate
        {
            PackageName = name,
            AvailableVersion = available,
            InstalledVersion = installed,
            Source = source,
        };
    }

    /// <summary>Builds the client under test over the given stub.</summary>
    /// <param name="stub">The stubbed transport.</param>
    /// <returns>The client.</returns>
    private static AgentMonitorClient Client(StubMonitorService stub)
    {
        return new AgentMonitorClient(stub, NullLogger<AgentMonitorClient>.Instance);
    }
}
