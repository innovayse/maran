using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Maps the agent's monitoring rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// Same shape as the other agent clients: the failure branch of the response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text is logged rather
/// than returned (rules/security.md item 8).
///
/// <c>ServiceStatus.uptime_seconds</c> and <c>AccountDiskUsage.quota_bytes</c> are reserved in the
/// contract (revision 13) and have no member in the generated types: the agent wrote 0 into both,
/// and a 0 copied into a panel type is indistinguishable from a measurement. The panel owns the
/// quota outright, and nothing needs the uptime.
///
/// One deprecated field — <c>ServiceStatus.running</c> — IS read, but only where <c>state</c> is absent. See
/// <see cref="ServiceStatusMapper.ToPanelState"/>: everywhere else the boolean is the two-valued reading the
/// tri-state replaced, and reading it would put every socket-activated unit back in the "stopped" column.
/// </remarks>
public sealed class AgentMonitorClient : IAgentMonitorClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IMonitorServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentMonitorClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentMonitorClient(IMonitorServiceInvoker invoker, ILogger<AgentMonitorClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentMonitorClient(IAgentChannels channels, ILogger<AgentMonitorClient> logger)
        : this(new GrpcMonitorServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentMonitorClient(GrpcChannel channel, ILogger<AgentMonitorClient> logger)
        : this(new FixedAgentChannels(channel), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.GetHostMetricsAsync(new GetHostMetricsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            GetHostMetricsResponse.ResultOneofCase.Ok => Result<AgentHostMetrics>.Ok(ToMetrics(response.Ok)),
            GetHostMetricsResponse.ResultOneofCase.Error => Result<AgentHostMetrics>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetHostMetricsAsync))),
            _ => Result<AgentHostMetrics>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.GetServiceStatusesAsync(new GetServiceStatusesRequest(), cancellationToken);

        return response.ResultCase switch
        {
            GetServiceStatusesResponse.ResultOneofCase.Ok => ServiceStatusMapper.ToStatusesResult(response.Ok),
            GetServiceStatusesResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentServiceStatus>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetServiceStatusesAsync))),
            _ => Result<IReadOnlyList<AgentServiceStatus>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        IReadOnlyCollection<string> accountUsernames,
        CancellationToken cancellationToken)
    {
        // An empty name list means EVERY account on the wire, which is not what this client's
        // caller asked for: nothing named is nothing to measure (see the interface's remarks).
        if (accountUsernames.Count == 0)
        {
            return Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok([]);
        }

        var request = new GetAccountsDiskUsageRequest();
        request.AccountUsernames.AddRange(accountUsernames);

        var response = await _invoker.GetAccountsDiskUsageAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            GetAccountsDiskUsageResponse.ResultOneofCase.Ok => Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok(
                ToDiskUsage(response.Ok)),
            GetAccountsDiskUsageResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentAccountDiskUsage>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetAccountsDiskUsageAsync))),
            _ => Result<IReadOnlyList<AgentAccountDiskUsage>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentSftpJailStatus>> GetSftpJailStatusAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.GetSftpJailStatusAsync(new GetSftpJailStatusRequest(), cancellationToken);

        return response.ResultCase switch
        {
            GetSftpJailStatusResponse.ResultOneofCase.Ok => ToJailStatus(response.Ok),
            GetSftpJailStatusResponse.ResultOneofCase.Error => Result<AgentSftpJailStatus>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetSftpJailStatusAsync))),
            _ => Result<AgentSftpJailStatus>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentQuotaEnforceability>> GetQuotaEnforceabilityAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.GetQuotaEnforceabilityAsync(
            new GetQuotaEnforceabilityRequest(),
            cancellationToken);

        return response.ResultCase switch
        {
            GetQuotaEnforceabilityResponse.ResultOneofCase.Ok => ToQuotaEnforceability(response.Ok),
            GetQuotaEnforceabilityResponse.ResultOneofCase.Error => Result<AgentQuotaEnforceability>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetQuotaEnforceabilityAsync))),
            _ => Result<AgentQuotaEnforceability>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentClockSyncState>> GetClockSyncAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.GetClockSyncAsync(new GetClockSyncRequest(), cancellationToken);

        return response.ResultCase switch
        {
            GetClockSyncResponse.ResultOneofCase.Ok => ToClockSyncState(response.Ok.State),
            GetClockSyncResponse.ResultOneofCase.Error => Result<AgentClockSyncState>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetClockSyncAsync))),
            _ => Result<AgentClockSyncState>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentServerFingerprint>> GetServerFingerprintAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.GetServerFingerprintInputsAsync(
            new GetServerFingerprintInputsRequest(),
            cancellationToken);

        return response.ResultCase switch
        {
            GetServerFingerprintInputsResponse.ResultOneofCase.Ok => Result<AgentServerFingerprint>.Ok(
                ToServerFingerprint(response.Ok)),
            GetServerFingerprintInputsResponse.ResultOneofCase.Error => Result<AgentServerFingerprint>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetServerFingerprintAsync))),
            _ => Result<AgentServerFingerprint>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentPendingUpdate>>> ListPendingUpdatesAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.ListPendingUpdatesAsync(new ListPendingUpdatesRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ListPendingUpdatesResponse.ResultOneofCase.Ok => Result<IReadOnlyList<AgentPendingUpdate>>.Ok(
                AgentPendingUpdateMapper.ToPendingUpdates(response.Ok)),
            ListPendingUpdatesResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentPendingUpdate>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListPendingUpdatesAsync))),
            _ => Result<IReadOnlyList<AgentPendingUpdate>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentRebootRequirement>> GetRebootRequirementAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.GetRebootRequirementAsync(new GetRebootRequirementRequest(), cancellationToken);

        return response.ResultCase switch
        {
            GetRebootRequirementResponse.ResultOneofCase.Ok => Result<AgentRebootRequirement>.Ok(
                RebootRequirementMapper.ToRebootRequirement(response.Ok.Requirement)),
            GetRebootRequirementResponse.ResultOneofCase.Error => Result<AgentRebootRequirement>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetRebootRequirementAsync))),
            _ => Result<AgentRebootRequirement>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Projects the wire pair onto the panel's own, keeping "there is none" a state.</summary>
    /// <param name="ok">The success payload of <c>GetServerFingerprintInputs</c>.</param>
    /// <returns>The two values, each null where the host has none.</returns>
    /// <remarks>
    /// <c>present</c> is what decides, never the string: a value arriving non-empty with
    /// <c>present</c> false would be a bug on the far side, and reading it anyway would turn that
    /// bug into a machine identity the panel then bound a paid licence to.
    /// </remarks>
    private static AgentServerFingerprint ToServerFingerprint(GetServerFingerprintInputsOk ok)
    {
        return new AgentServerFingerprint(
            ok.MachineId is { Present: true } machineId ? machineId.Value : null,
            ok.PrimaryInterface is { Present: true } primaryInterface ? primaryInterface.Value : null);
    }

    /// <summary>Projects the wire clock state onto the panel's own.</summary>
    /// <param name="state">The state the agent reported.</param>
    /// <returns>The panel's state, or <c>AgentInvalidResponse</c> for a value it cannot name.</returns>
    /// <remarks>
    /// Written out rather than cast: UNSPECIFIED, or a value from an agent newer than this panel, is
    /// a reply that says nothing, so it is refused rather than read as either answer — neither
    /// "synchronised" nor "not synchronised" may be invented for it.
    /// </remarks>
    private static Result<AgentClockSyncState> ToClockSyncState(ClockSyncState state)
    {
        return state switch
        {
            ClockSyncState.Synchronized => Result<AgentClockSyncState>.Ok(AgentClockSyncState.Synchronized),
            ClockSyncState.NotSynchronized => Result<AgentClockSyncState>.Ok(AgentClockSyncState.NotSynchronized),
            ClockSyncState.SyncServiceDisabled => Result<AgentClockSyncState>.Ok(
                AgentClockSyncState.SyncServiceDisabled),
            _ => Result<AgentClockSyncState>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Projects the wire finding onto the panel's own.</summary>
    /// <param name="ok">The success payload of <c>GetQuotaEnforceability</c>.</param>
    /// <returns>
    /// Whether the filesystem can enforce a quota, and why not when it cannot — or
    /// <c>AgentInvalidResponse</c> when the wire states neither.
    /// </returns>
    /// <remarks>
    /// Written out rather than cast. UNSPECIFIED means the agent predates the field, and an
    /// unrecognised value means it is newer than this panel; the handshake window admits neither.
    /// Such a reply says nothing, so it is refused as a failed read: the caller already treats a
    /// failed read as "no evidence either way" (see <see cref="IAgentMonitorClient"/>), which is the
    /// honest reading, whereas answering "enforceable" would display a limit nothing backs and
    /// answering "not enforceable" would raise an alert about a fact nobody measured. The same goes
    /// for a NOT_ENFORCEABLE finding whose reason the panel cannot name: the reason is part of the
    /// finding, so a finding without one is not a finding.
    /// </remarks>
    private static Result<AgentQuotaEnforceability> ToQuotaEnforceability(GetQuotaEnforceabilityOk ok)
    {
        switch (ok.Enforceability)
        {
            case QuotaEnforceability.Enforceable:
                return Result<AgentQuotaEnforceability>.Ok(new AgentQuotaEnforceability.Enforceable());
            case QuotaEnforceability.NotEnforceable:
                return ToNotEnforceable(ok.Reason);
            default:
                return Result<AgentQuotaEnforceability>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }
    }

    /// <summary>Names the reason the agent gave for a filesystem that cannot enforce a quota.</summary>
    /// <param name="reason">The wire reason.</param>
    /// <returns>The finding, or <c>AgentInvalidResponse</c> when the wire names no reason this panel knows.</returns>
    private static Result<AgentQuotaEnforceability> ToNotEnforceable(QuotaEnforceabilityReason reason)
    {
        switch (reason)
        {
            case QuotaEnforceabilityReason.MountedWithoutQuotaAccounting:
                return Result<AgentQuotaEnforceability>.Ok(
                    new AgentQuotaEnforceability.NotEnforceable(
                        AccountsService.QuotaUnenforceableReason.MountedWithoutQuotaAccounting));
            case QuotaEnforceabilityReason.AccountingNotEnabled:
                return Result<AgentQuotaEnforceability>.Ok(
                    new AgentQuotaEnforceability.NotEnforceable(
                        AccountsService.QuotaUnenforceableReason.AccountingNotEnabled));
            default:
                return Result<AgentQuotaEnforceability>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }
    }

    /// <summary>Projects the wire finding onto the panel's own.</summary>
    /// <param name="ok">The success payload of <c>GetSftpJailStatus</c>.</param>
    /// <returns>
    /// Whether the block is intact, and what is missing when it is not — or <c>AgentInvalidResponse</c>
    /// when the wire states neither.
    /// </returns>
    /// <remarks>
    /// Written out rather than cast, for the same reason <see cref="ServiceStatusMapper.ToPanelState"/> is: a
    /// value this build has no name for — UNSPECIFIED from an agent that predates the field, or a third state
    /// from one newer than this panel — must not silently become a member the switch below never
    /// intended. It is refused as a failed read instead of being read as drifted: a failed read
    /// advances no alert, whereas "drifted" with an empty list of what is missing would raise one
    /// about a finding nobody made.
    /// </remarks>
    private static Result<AgentSftpJailStatus> ToJailStatus(GetSftpJailStatusOk ok)
    {
        switch (ok.State)
        {
            case SftpJailState.Intact:
                return Result<AgentSftpJailStatus>.Ok(new AgentSftpJailStatus.Intact());
            case SftpJailState.Drifted:
                return Result<AgentSftpJailStatus>.Ok(new AgentSftpJailStatus.Drifted([.. ok.Missing]));
            default:
                return Result<AgentSftpJailStatus>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }
    }

    /// <summary>Projects the wire snapshot onto the panel's own.</summary>
    /// <param name="metrics">The success payload of <c>GetHostMetrics</c>.</param>
    /// <returns>The same twelve figures, unaltered.</returns>
    /// <remarks>
    /// Nothing is derived here. The two network counters in particular are passed through as the
    /// counters they are: turning a pair of them into a rate needs the seconds that elapsed between
    /// two readings, which this call does not have and must not guess.
    /// </remarks>
    private static AgentHostMetrics ToMetrics(HostMetrics metrics)
    {
        return new AgentHostMetrics(
            metrics.CpuPercent,
            metrics.MemoryUsedBytes,
            metrics.MemoryTotalBytes,
            metrics.DiskUsedBytes,
            metrics.DiskReservedBytes,
            metrics.DiskAvailableBytes,
            metrics.DiskTotalBytes,
            metrics.NetworkRxBytes,
            metrics.NetworkTxBytes,
            metrics.LoadAverage1M,
            metrics.LoadAverage5M,
            metrics.LoadAverage15M,
            metrics.FreeSpaceFloorBytes);
    }

    /// <summary>Projects the wire disk-usage listing onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>GetAccountsDiskUsage</c>.</param>
    /// <returns>One row per account, carrying used bytes only.</returns>
    private static List<AgentAccountDiskUsage> ToDiskUsage(GetAccountsDiskUsageOk ok)
    {
        var usage = new List<AgentAccountDiskUsage>(ok.Accounts.Count);

        foreach (var account in ok.Accounts)
        {
            usage.Add(new AgentAccountDiskUsage(account.AccountUsername, account.UsedBytes));
        }

        return usage;
    }
}
