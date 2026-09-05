using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.MonitorService;

/// <summary>Mapping contract of AgentMonitorClient, and the tri-state reading it must not collapse.</summary>
public sealed class AgentMonitorClientTests
{
    /// <summary>The metrics ok payload maps all ten figures unaltered.</summary>
    [Fact]
    public async Task The_metrics_ok_payload_maps_all_ten_figures_unaltered()
    {
        var stub = new StubMonitorService
        {
            MetricsResponse = new GetHostMetricsResponse
            {
                Ok = new HostMetrics
                {
                    CpuPercent = 12.5,
                    MemoryUsedBytes = 2_000_000_000,
                    MemoryTotalBytes = 8_000_000_000,
                    DiskUsedBytes = 30_000_000_000,
                    DiskTotalBytes = 100_000_000_000,
                    NetworkRxBytes = 5_500,
                    NetworkTxBytes = 900,
                    LoadAverage1M = 0.42,
                    LoadAverage5M = 0.31,
                    LoadAverage15M = 0.20,
                },
            },
        };

        var result = await Client(stub).GetHostMetricsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new AgentHostMetrics(
                12.5,
                2_000_000_000,
                8_000_000_000,
                30_000_000_000,
                100_000_000_000,
                5_500,
                900,
                0.42,
                0.31,
                0.20),
            result.Value);
        Assert.NotNull(stub.LastMetricsRequest);
    }

    /// <summary>The metrics error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_metrics_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubMonitorService
        {
            MetricsResponse = new GetHostMetricsResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "cannot read /proc/stat" },
            },
        };

        var result = await Client(stub).GetHostMetricsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>The agents diagnostic text is logged and never carried back to the caller.</summary>
    [Fact]
    public async Task The_agents_diagnostic_text_is_logged_and_never_carried_back_to_the_caller()
    {
        var logger = new RecordingLogger<AgentMonitorClient>();
        var stub = new StubMonitorService
        {
            MetricsResponse = new GetHostMetricsResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "cannot read /proc/meminfo" },
            },
        };

        var result = await new AgentMonitorClient(stub, logger).GetHostMetricsAsync(CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.DoesNotContain("/proc", result.Error.Code, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/proc/meminfo", logged, StringComparison.Ordinal);
    }

    /// <summary>A metrics response with neither branch set is refused rather than read as an idle host.</summary>
    /// <remarks>
    /// The proto3 defaults of this message are a host using no CPU, no memory and no disk — a
    /// perfectly healthy-looking snapshot of a server nobody measured.
    /// </remarks>
    [Fact]
    public async Task A_metrics_response_with_neither_branch_set_is_refused_rather_than_read_as_an_idle_host()
    {
        var result = await Client(new StubMonitorService()).GetHostMetricsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A unit the agent cannot call up or down stays not known and never becomes stopped.</summary>
    /// <remarks>
    /// The reason the state has three values. On the Debian family the enabled SSH unit is a socket
    /// and the service it fronts is inactive from boot until the first connection, so a panel that
    /// read "not known" as an outage would e-mail about one on every such host at every reboot — and
    /// an alert nobody can act on teaches an operator to ignore the rest.
    /// </remarks>
    [Fact]
    public async Task A_unit_the_agent_cannot_call_up_or_down_stays_not_known_and_never_becomes_stopped()
    {
        var stub = StubMonitorService.ReportingOneService(
            ManagedService.Ssh,
            ServiceState.Unknown,
            false,
            "not yet started; ssh.socket is listening for it");

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        var status = Assert.Single(result.Value);
        Assert.Equal(AgentServiceState.Unknown, status.State);
        Assert.NotEqual(AgentServiceState.Stopped, status.State);
        Assert.Equal(AgentManagedService.Ssh, status.Service);
        Assert.Equal("not yet started; ssh.socket is listening for it", status.Detail);
    }

    /// <summary>A reported state is read from the state field even when the deprecated boolean contradicts it.</summary>
    /// <param name="state">The state the agent reports.</param>
    /// <param name="running">The deprecated boolean, set to disagree with that state.</param>
    /// <param name="expected">What the panel must read, which is the state and never the boolean.</param>
    /// <remarks>
    /// The three states above UNSPECIFIED must ignore <c>running</c> entirely, and every row here
    /// sets it to the value that would flip the answer if it were consulted. Without this, a mapping
    /// that quietly fell back to the boolean inside the UNKNOWN arm passes every other test in this
    /// file — a measured surviving mutant, found in review — because those tests set the boolean to
    /// agree with the state. Consulting it is exactly the drift the tri-state was added to end: the
    /// boolean is false for STOPPED and for UNKNOWN alike, so a socket-activated unit would page an
    /// operator again the moment it crept back in.
    /// </remarks>
    [Theory]
    [InlineData(ServiceState.Running, false, AgentServiceState.Running)]
    [InlineData(ServiceState.Stopped, true, AgentServiceState.Stopped)]
    [InlineData(ServiceState.Unknown, true, AgentServiceState.Unknown)]
    public async Task A_reported_state_is_read_from_the_state_field_even_when_the_boolean_contradicts_it(
        ServiceState state,
        bool running,
        AgentServiceState expected)
    {
        var stub = StubMonitorService.ReportingOneService(ManagedService.Ssh, state, running, "active (running)");

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result.Value).State);
    }

    /// <summary>A running unit is running and a stopped one is stopped.</summary>
    /// <param name="state">The state the agent reports.</param>
    /// <param name="expected">What the panel must read it as.</param>
    [Theory]
    [InlineData(ServiceState.Running, AgentServiceState.Running)]
    [InlineData(ServiceState.Stopped, AgentServiceState.Stopped)]
    public async Task A_running_unit_is_running_and_a_stopped_one_is_stopped(
        ServiceState state,
        AgentServiceState expected)
    {
        var stub = StubMonitorService.ReportingOneService(
            ManagedService.WebServer,
            state,
            state == ServiceState.Running,
            "active (running)");

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result.Value).State);
    }

    /// <summary>An agent too old to send a state is read through the boolean it did send.</summary>
    /// <param name="running">The deprecated boolean the old agent wrote.</param>
    /// <param name="expected">What the panel must read it as.</param>
    /// <remarks>
    /// The one place the deprecated boolean is read. An older agent sends the proto3 default for the
    /// state field, and taking that at face value would report every service on that host as not
    /// known — hiding a real outage behind a version difference. Within this arm the boolean is the
    /// whole of what was ever known, so false is the two-valued contract's own "stopped".
    /// </remarks>
    [Theory]
    [InlineData(true, AgentServiceState.Running)]
    [InlineData(false, AgentServiceState.Stopped)]
    public async Task An_agent_too_old_to_send_a_state_is_read_through_the_boolean_it_did_send(
        bool running,
        AgentServiceState expected)
    {
        var stub = StubMonitorService.ReportingOneService(
            ManagedService.Database,
            ServiceState.Unspecified,
            running,
            string.Empty);

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result.Value).State);
    }

    /// <summary>A state this build has never heard of is not known rather than resolved through the boolean.</summary>
    /// <remarks>
    /// A newer agent's fourth state says something this panel cannot interpret, and what the boolean
    /// means beside it is exactly what nobody can say. Reading it would be guessing, and the guess
    /// would be about whether to wake somebody.
    /// </remarks>
    [Fact]
    public async Task A_state_this_build_has_never_heard_of_is_not_known_rather_than_resolved_through_the_boolean()
    {
        var stub = StubMonitorService.ReportingOneService(
            ManagedService.WebServer,
            (ServiceState)99,
            true,
            "something newer");

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        Assert.Equal(AgentServiceState.Unknown, Assert.Single(result.Value).State);
    }

    /// <summary>Every service the wire names has a panel name and keeps its number.</summary>
    /// <param name="wire">The value the agent sent.</param>
    /// <param name="expected">The panel member it must arrive as.</param>
    [Theory]
    [InlineData(ManagedService.WebServer, AgentManagedService.WebServer)]
    [InlineData(ManagedService.PhpFpm, AgentManagedService.PhpFpm)]
    [InlineData(ManagedService.Database, AgentManagedService.Database)]
    [InlineData(ManagedService.Ftp, AgentManagedService.Ftp)]
    [InlineData(ManagedService.Cron, AgentManagedService.Cron)]
    [InlineData(ManagedService.Ssh, AgentManagedService.Ssh)]
    public async Task Every_service_the_wire_names_has_a_panel_name_and_keeps_its_number(
        ManagedService wire,
        AgentManagedService expected)
    {
        var stub = StubMonitorService.ReportingOneService(wire, ServiceState.Running, true, "active");

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result.Value).Service);
    }

    /// <summary>A service this build has no name for arrives unspecified and never as another service.</summary>
    /// <remarks>
    /// Written out rather than cast, so a newer agent watching a unit this panel predates cannot hand
    /// callers an enum value outside the declared set — every switch over which would fall silently
    /// through all its arms.
    /// </remarks>
    [Fact]
    public async Task A_service_this_build_has_no_name_for_arrives_unspecified_and_never_as_another_service()
    {
        var stub = StubMonitorService.ReportingOneService((ManagedService)99, ServiceState.Running, true, "active");

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        Assert.Equal(AgentManagedService.Unspecified, Assert.Single(result.Value).Service);
    }

    /// <summary>The statuses error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_statuses_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubMonitorService
        {
            StatusesResponse = new GetServiceStatusesResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "systemd unreachable" },
            },
        };

        var result = await Client(stub).GetServiceStatusesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A statuses response with neither branch set is refused rather than read as no services.</summary>
    /// <remarks>
    /// An empty list means "this agent watches nothing", which a caller is told to read as not known
    /// — so a response that says nothing at all must not become one, or a monitor would report a
    /// broken agent as a host with nothing to watch.
    /// </remarks>
    [Fact]
    public async Task A_statuses_response_with_neither_branch_set_is_refused_rather_than_read_as_no_services()
    {
        var result = await Client(new StubMonitorService()).GetServiceStatusesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

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

        var result = await Client(stub).GetAccountsDiskUsageAsync(CancellationToken.None);

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

        var result = await Client(stub).GetAccountsDiskUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A disk usage response with neither branch set is refused rather than read as no accounts.</summary>
    [Fact]
    public async Task A_disk_usage_response_with_neither_branch_set_is_refused_rather_than_read_as_no_accounts()
    {
        var result = await Client(new StubMonitorService()).GetAccountsDiskUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds the production client over a stub transport and a logger nothing asserts.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>The client under test.</returns>
    private static AgentMonitorClient Client(StubMonitorService stub)
    {
        return new AgentMonitorClient(stub, NullLogger<AgentMonitorClient>.Instance);
    }
}
