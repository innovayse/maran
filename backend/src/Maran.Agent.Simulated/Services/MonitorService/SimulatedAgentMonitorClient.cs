using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Usage;

namespace Maran.Agent.Simulated.Services.MonitorService;

/// <summary>
/// The agent's readings of the host, produced instead of measured: load and capacity follow a
/// deterministic wave of the injected clock, account disk use follows each account's own curve, and
/// everything else is read from the host facts the ledger holds.
/// </summary>
/// <remarks>
/// Read-only: no method changes the ledger. Because the facts are state a seed can replace, a
/// demonstration can show a host that is waiting for a reboot, has a stopped service or an
/// unsynchronised clock without any change here.
/// </remarks>
public sealed class SimulatedAgentMonitorClient : IAgentMonitorClient
{
    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>The source of the current moment.</summary>
    private readonly IClock _clock;

    /// <summary>Creates a client over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    /// <param name="clock">The source of the moment a reading is taken at.</param>
    public SimulatedAgentMonitorClient(SimulatedAgentLedger ledger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);

        _ledger = ledger;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _clock.UtcNow;
        var accountsUsed = 0UL;
        foreach (var account in _ledger.Accounts.Snapshot())
        {
            accountsUsed += SimulatedUsageCurve.AccountUsedBytes(account, now);
        }

        var metrics = SimulatedUsageCurve.HostMetrics(Facts(), accountsUsed, now);

        return Task.FromResult(Result<AgentHostMetrics>.Ok(metrics));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<IReadOnlyList<AgentServiceStatus>>.Ok(Facts().Services));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        IReadOnlyCollection<string> accountUsernames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(accountUsernames);

        var now = _clock.UtcNow;
        var rows = new List<AgentAccountDiskUsage>();
        foreach (var username in accountUsernames.Distinct(StringComparer.Ordinal))
        {
            // A named account the host does not hold is absent from the answer, never a zero row.
            if (_ledger.Accounts.TryGet(username, out var account))
            {
                rows.Add(new AgentAccountDiskUsage(username, SimulatedUsageCurve.AccountUsedBytes(account, now)));
            }
        }

        return Task.FromResult(Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok(rows));
    }

    /// <inheritdoc />
    public Task<Result<AgentSftpJailStatus>> GetSftpJailStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // There is no sshd_config to drift, so the jail block is always the installer's own.
        return Task.FromResult(Result<AgentSftpJailStatus>.Ok(new AgentSftpJailStatus.Intact()));
    }

    /// <inheritdoc />
    public Task<Result<AgentQuotaEnforceability>> GetQuotaEnforceabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentQuotaEnforceability>.Ok(new AgentQuotaEnforceability.Enforceable()));
    }

    /// <inheritdoc />
    public Task<Result<AgentClockSyncState>> GetClockSyncAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentClockSyncState>.Ok(Facts().ClockSync));
    }

    /// <inheritdoc />
    public Task<Result<AgentServerFingerprint>> GetServerFingerprintAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var facts = Facts();

        return Task.FromResult(
            Result<AgentServerFingerprint>.Ok(new AgentServerFingerprint(facts.MachineId, facts.PrimaryInterface)));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentPendingUpdate>>> ListPendingUpdatesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<IReadOnlyList<AgentPendingUpdate>>.Ok(Facts().PendingUpdates));
    }

    /// <inheritdoc />
    public Task<Result<AgentRebootRequirement>> GetRebootRequirementAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentRebootRequirement>.Ok(Facts().Reboot));
    }

    /// <summary>Reads the host's facts, which are the baseline until a seed replaces them.</summary>
    /// <returns>The facts in force.</returns>
    private SimulatedHostFacts Facts()
    {
        return _ledger.HostFacts.ValueOr(SimulatedHostFacts.Baseline);
    }
}
