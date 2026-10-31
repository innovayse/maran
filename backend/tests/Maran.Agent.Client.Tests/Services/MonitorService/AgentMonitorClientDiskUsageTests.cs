using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.MonitorService;

/// <summary>
/// Mapping contract of <see cref="AgentMonitorClient.GetAccountsDiskUsageAsync"/>: which names reach the
/// wire, what an empty list means on each side of it, and how each answer maps.
/// </summary>
public sealed class AgentMonitorClientDiskUsageTests
{
    /// <summary>Disk usage carries the used bytes of every account and never the deprecated quota.</summary>
    /// <remarks>
    /// The agent writes <c>quota_bytes</c> as 0 and the panel owns the real figure, so the wire value
    /// is set here to something that could not be a used size: if it ever reached the panel's row the
    /// assertion below would be reading it.
    /// </remarks>
    [Fact]
    public async Task Disk_usage_carries_the_used_bytes_of_every_account_and_never_the_deprecated_quota()
    {
        var stub = new StubMonitorService
        {
            DiskUsageResponse = new GetAccountsDiskUsageResponse
            {
                Ok = new GetAccountsDiskUsageOk
                {
                    Accounts =
                    {
                        new AccountDiskUsage
                        {
                            AccountUsername = "alice",
                            UsedBytes = 1_048_576,
                            QuotaBytes = 999_999_999,
                        },
                        new AccountDiskUsage { AccountUsername = "bob", UsedBytes = 0 },
                    },
                },
            },
        };

        var result = await Client(stub).GetAccountsDiskUsageAsync(["alice", "bob"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AgentAccountDiskUsage("alice", 1_048_576), result.Value[0]);
        Assert.Equal(new AgentAccountDiskUsage("bob", 0), result.Value[1]);
        Assert.NotNull(stub.LastDiskUsageRequest);
    }

    /// <summary>The disk usage error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_disk_usage_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubMonitorService
        {
            DiskUsageResponse = new GetAccountsDiskUsageResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "cannot stat /home" },
            },
        };

        var result = await Client(stub).GetAccountsDiskUsageAsync(["alice", "bob"], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A disk usage response with neither branch set is refused rather than read as no accounts.</summary>
    [Fact]
    public async Task A_disk_usage_response_with_neither_branch_set_is_refused_rather_than_read_as_no_accounts()
    {
        var result = await Client(new StubMonitorService()).GetAccountsDiskUsageAsync(["alice", "bob"], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The names asked about travel to the agent exactly, in the order given.</summary>
    [Fact]
    public async Task The_names_asked_about_travel_to_the_agent_exactly_in_the_order_given()
    {
        var stub = new StubMonitorService
        {
            DiskUsageResponse = new GetAccountsDiskUsageResponse { Ok = new GetAccountsDiskUsageOk() },
        };

        await Client(stub).GetAccountsDiskUsageAsync(["carol", "alice"], CancellationToken.None);

        Assert.Equal(["carol", "alice"], stub.LastDiskUsageRequest!.AccountUsernames);
    }

    /// <summary>
    /// No names measures nothing and never reaches the agent, because an empty list on the wire is a walk
    /// of every home on the host.
    /// </summary>
    [Fact]
    public async Task No_names_measures_nothing_and_never_reaches_the_agent()
    {
        var stub = new StubMonitorService();

        var result = await Client(stub).GetAccountsDiskUsageAsync([], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Null(stub.LastDiskUsageRequest);
    }

    /// <summary>Builds the client under test over the given stub.</summary>
    /// <param name="stub">The stubbed transport.</param>
    /// <returns>The client.</returns>
    private static AgentMonitorClient Client(StubMonitorService stub)
    {
        return new AgentMonitorClient(stub, NullLogger<AgentMonitorClient>.Instance);
    }
}
