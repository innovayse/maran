using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.Simulated.Services.MonitorService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;
using Maran.Agent.Simulated.Usage;

namespace Maran.Agent.Simulated.Tests.Services.MonitorService;

/// <summary>The agent's readings of the host are produced from a clock and the ledger, never measured.</summary>
public sealed class SimulatedAgentMonitorClientTests
{
    /// <summary>Bytes in a gibibyte.</summary>
    private const ulong Gibibyte = 1024UL * 1024 * 1024;

    /// <summary>The source of the current moment.</summary>
    private readonly FixedClock _clock = new();

    /// <summary>Two readings at one moment are identical, so a screen does not flicker between refreshes.</summary>
    [Fact]
    public async Task Host_metrics_are_the_same_at_the_same_moment()
    {
        var client = NewClient(SimulatedHosts.NewLedger());

        var first = (await client.GetHostMetricsAsync(CancellationToken.None)).Value;
        var second = (await client.GetHostMetricsAsync(CancellationToken.None)).Value;

        Assert.Equal(first, second);
    }

    /// <summary>The figures stay inside the ranges the curves promise and are consistent with each other.</summary>
    [Fact]
    public async Task Host_metrics_stay_inside_their_documented_ranges()
    {
        var client = NewClient(SimulatedHosts.NewLedger());

        for (var minute = 0; minute < 120; minute += 7)
        {
            _clock.UtcNow = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero).AddMinutes(minute);

            var metrics = (await client.GetHostMetricsAsync(CancellationToken.None)).Value;

            Assert.InRange(metrics.CpuPercent, 8.0, 20.0);
            Assert.InRange(
                metrics.MemoryUsedBytes,
                (8 * Gibibyte * 30 / 100) - 16,
                (8 * Gibibyte * 35 / 100) + 16);
            Assert.Equal(8 * Gibibyte, metrics.MemoryTotalBytes);
            Assert.True(metrics.DiskUsedBytes + metrics.DiskReservedBytes + metrics.DiskAvailableBytes
                <= metrics.DiskTotalBytes);
            Assert.True(metrics.LoadAverage1m >= metrics.LoadAverage5m);
            Assert.True(metrics.LoadAverage5m >= metrics.LoadAverage15m);
        }
    }

    /// <summary>Network counters are totals since boot, so they never go backward as time passes.</summary>
    [Fact]
    public async Task Network_counters_only_grow_as_the_clock_moves_forward()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        var before = (await client.GetHostMetricsAsync(CancellationToken.None)).Value;

        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        var after = (await client.GetHostMetricsAsync(CancellationToken.None)).Value;

        Assert.True(after.NetworkRxBytes > before.NetworkRxBytes);
        Assert.True(after.NetworkTxBytes > before.NetworkTxBytes);
    }

    /// <summary>The disk the accounts use is part of the disk the host reports as used.</summary>
    [Fact]
    public async Task Hosted_accounts_add_to_the_disk_the_host_reports_as_used()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        var empty = (await client.GetHostMetricsAsync(CancellationToken.None)).Value;

        SimulatedHosts.AddAccount(ledger, "acme", _clock.UtcNow);
        var withAccount = (await client.GetHostMetricsAsync(CancellationToken.None)).Value;

        Assert.True(withAccount.DiskUsedBytes > empty.DiskUsedBytes);
    }

    /// <summary>Every service the panel watches is reported, and a baseline host runs them all.</summary>
    [Fact]
    public async Task A_baseline_host_reports_every_managed_service_running()
    {
        var client = NewClient(SimulatedHosts.NewLedger());

        var statuses = (await client.GetServiceStatusesAsync(CancellationToken.None)).Value;

        Assert.Equal(
            Enum.GetValues<AgentManagedService>().Order().ToList(),
            statuses.Select(status => { return status.Service; }).Order().ToList());
        Assert.All(statuses, status => { Assert.Equal(AgentServiceState.Running, status.State); });
    }

    /// <summary>A seed can describe a sick host, and the readings follow it without any change to the client.</summary>
    [Fact]
    public async Task Replacing_the_host_facts_changes_what_every_reading_reports()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        var sick = SimulatedHostFacts.Baseline with
        {
            Reboot = AgentRebootRequirement.Required,
            ClockSync = AgentClockSyncState.NotSynchronized,
            MachineId = "feedface",
            PendingUpdates = [],
            Services = [new AgentServiceStatus(AgentManagedService.Ftp, AgentServiceState.Stopped, "inactive")],
        };

        ledger.HostFacts.Replace(sick);

        var reboot = await client.GetRebootRequirementAsync(CancellationToken.None);
        var clock = await client.GetClockSyncAsync(CancellationToken.None);
        var fingerprint = await client.GetServerFingerprintAsync(CancellationToken.None);
        var updates = await client.ListPendingUpdatesAsync(CancellationToken.None);
        var services = await client.GetServiceStatusesAsync(CancellationToken.None);

        Assert.Equal(AgentRebootRequirement.Required, reboot.Value);
        Assert.Equal(AgentClockSyncState.NotSynchronized, clock.Value);
        Assert.Equal("feedface", fingerprint.Value.MachineId);
        Assert.Empty(updates.Value);
        Assert.Equal(AgentServiceState.Stopped, Assert.Single(services.Value).State);
    }

    /// <summary>The baseline host needs no reboot, keeps time, and has updates of both kinds waiting.</summary>
    [Fact]
    public async Task The_baseline_host_has_security_and_ordinary_updates_waiting()
    {
        var client = NewClient(SimulatedHosts.NewLedger());

        var updates = (await client.ListPendingUpdatesAsync(CancellationToken.None)).Value;
        var reboot = await client.GetRebootRequirementAsync(CancellationToken.None);
        var clock = await client.GetClockSyncAsync(CancellationToken.None);
        var fingerprint = await client.GetServerFingerprintAsync(CancellationToken.None);

        Assert.Contains(updates, update => { return update.Classification == AgentUpdateClassification.Security; });
        Assert.Contains(updates, update => { return update.Classification == AgentUpdateClassification.Ordinary; });
        Assert.Equal(AgentRebootRequirement.NotRequired, reboot.Value);
        Assert.Equal(AgentClockSyncState.Synchronized, clock.Value);
        Assert.False(string.IsNullOrEmpty(fingerprint.Value.MachineId));
        Assert.Equal("eth0", fingerprint.Value.PrimaryInterface);
    }

    /// <summary>Only held accounts are measured, each once, and an unknown name is absent rather than zero.</summary>
    [Fact]
    public async Task Disk_usage_covers_only_the_named_accounts_the_host_holds()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = NewClient(ledger);
        SimulatedHosts.AddAccount(ledger, "acme", _clock.UtcNow);
        SimulatedHosts.AddAccount(ledger, "globex", _clock.UtcNow);

        var answer = await client.GetAccountsDiskUsageAsync(["globex", "ghost", "globex"], CancellationToken.None);

        var only = Assert.Single(answer.Value);
        Assert.Equal("globex", only.AccountUsername);
        Assert.True(only.UsedBytes > 0);
    }

    /// <summary>An empty list measures nothing, not every account.</summary>
    [Fact]
    public async Task Disk_usage_of_no_names_is_an_empty_answer()
    {
        var ledger = SimulatedHosts.NewLedger();
        SimulatedHosts.AddAccount(ledger, "acme", _clock.UtcNow);

        var rows = await NewClient(ledger).GetAccountsDiskUsageAsync([], CancellationToken.None);

        Assert.Empty(rows.Value);
    }

    /// <summary>The usage reported per account is the same figure the accounts client reports.</summary>
    [Fact]
    public async Task Disk_usage_matches_the_accounts_own_usage_curve()
    {
        var ledger = SimulatedHosts.NewLedger();
        var account = SimulatedHosts.AddAccount(ledger, "acme", _clock.UtcNow.AddDays(-12));

        var rows = (await NewClient(ledger).GetAccountsDiskUsageAsync(["acme"], CancellationToken.None)).Value;

        Assert.Equal(
            SimulatedUsageCurve.AccountUsedBytes(account, _clock.UtcNow),
            Assert.Single(rows).UsedBytes);
    }

    /// <summary>There is no sshd_config to drift and the quota can always be enforced.</summary>
    [Fact]
    public async Task The_sftp_jail_is_intact_and_quotas_are_enforceable()
    {
        var client = NewClient(SimulatedHosts.NewLedger());

        var jail = await client.GetSftpJailStatusAsync(CancellationToken.None);
        var quota = await client.GetQuotaEnforceabilityAsync(CancellationToken.None);

        Assert.IsType<AgentSftpJailStatus.Intact>(jail.Value);
        Assert.IsType<AgentQuotaEnforceability.Enforceable>(quota.Value);
    }

    /// <summary>A caller that has already given up is told so, as with a real transport.</summary>
    [Fact]
    public async Task A_cancelled_reading_is_not_answered()
    {
        var client = NewClient(SimulatedHosts.NewLedger());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => { return client.GetHostMetricsAsync(cancelled.Token); });
    }

    /// <summary>Builds a client over <paramref name="ledger"/> and this test's clock.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    /// <returns>The client.</returns>
    private SimulatedAgentMonitorClient NewClient(SimulatedAgentLedger ledger)
    {
        return new SimulatedAgentMonitorClient(ledger, _clock);
    }
}
