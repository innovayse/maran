using Grpc.Net.Client;
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
/// Two wire fields are deliberately not projected. <c>ServiceStatus.uptime_seconds</c> and
/// <c>AccountDiskUsage.quota_bytes</c> are written 0 by the agent, and a 0 copied into a panel type
/// is indistinguishable from a measurement: "just restarted" and "no quota" are both alarming and
/// both untrue. The panel owns the quota outright, and nothing needs the uptime.
///
/// The third — <c>ServiceStatus.running</c> — IS read, but only where <c>state</c> is absent. See
/// <see cref="ToPanelState"/>: everywhere else the boolean is the two-valued reading the tri-state
/// replaced, and reading it would put every socket-activated unit back in the "stopped" column.
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

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentMonitorClient(GrpcChannel channel, ILogger<AgentMonitorClient> logger)
        : this(new GrpcMonitorServiceInvoker(new V1.MonitorService.MonitorServiceClient(channel)), logger)
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
            GetServiceStatusesResponse.ResultOneofCase.Ok => Result<IReadOnlyList<AgentServiceStatus>>.Ok(
                ToStatuses(response.Ok)),
            GetServiceStatusesResponse.ResultOneofCase.Error => Result<IReadOnlyList<AgentServiceStatus>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetServiceStatusesAsync))),
            _ => Result<IReadOnlyList<AgentServiceStatus>>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        CancellationToken cancellationToken)
    {
        var response = await _invoker.GetAccountsDiskUsageAsync(
            new GetAccountsDiskUsageRequest(),
            cancellationToken);

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

    /// <summary>Projects the wire snapshot onto the panel's own.</summary>
    /// <param name="metrics">The success payload of <c>GetHostMetrics</c>.</param>
    /// <returns>The same ten figures, unaltered.</returns>
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
            metrics.DiskTotalBytes,
            metrics.NetworkRxBytes,
            metrics.NetworkTxBytes,
            metrics.LoadAverage1M,
            metrics.LoadAverage5M,
            metrics.LoadAverage15M);
    }

    /// <summary>Projects the wire statuses onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>GetServiceStatuses</c>.</param>
    /// <returns>One row per service the agent reported, in the order it sent them.</returns>
    private static List<AgentServiceStatus> ToStatuses(GetServiceStatusesOk ok)
    {
        var statuses = new List<AgentServiceStatus>(ok.Services.Count);

        foreach (var status in ok.Services)
        {
            statuses.Add(new AgentServiceStatus(
                ToPanelService(status.Service),
                ToPanelState(status),
                status.Detail));
        }

        return statuses;
    }

    /// <summary>Maps a wire service onto the panel's own.</summary>
    /// <param name="service">The value the agent sent.</param>
    /// <returns>
    /// The matching member, or <see cref="AgentManagedService.Unspecified"/> for a value this build
    /// has no name for.
    /// </returns>
    /// <remarks>
    /// Written out rather than cast. A cast would hand callers an enum value outside the declared
    /// set the first time a newer agent watches a unit this panel predates, and every switch over
    /// the result would then fall through its arms silently.
    /// </remarks>
    private static AgentManagedService ToPanelService(ManagedService service)
    {
        return service switch
        {
            ManagedService.WebServer => AgentManagedService.WebServer,
            ManagedService.PhpFpm => AgentManagedService.PhpFpm,
            ManagedService.Database => AgentManagedService.Database,
            ManagedService.Ftp => AgentManagedService.Ftp,
            ManagedService.Cron => AgentManagedService.Cron,
            ManagedService.Ssh => AgentManagedService.Ssh,
            _ => AgentManagedService.Unspecified,
        };
    }

    /// <summary>Reads one row's state, resolving an agent too old to have sent one.</summary>
    /// <param name="status">The row the agent sent.</param>
    /// <returns>Up, down, or not known.</returns>
    /// <remarks>
    /// <c>state</c> is the field to read, and UNKNOWN is carried through as UNKNOWN. Mapping it onto
    /// <see cref="AgentServiceState.Stopped"/> is the specific mistake this contract was widened to
    /// prevent: on the Debian family the enabled SSH unit is a socket and the service it fronts is
    /// inactive from boot until the first connection, so a panel that read that as an outage would
    /// e-mail about one on every such host at every reboot — and the alert nobody can act on is the
    /// alert that teaches an operator to ignore the rest.
    ///
    /// The unspecified arm is the ONE place the deprecated <c>running</c> boolean is read, and it is
    /// read because an agent that predates the state field sends the proto3 default and did send the
    /// boolean: taking UNSPECIFIED at face value there would report every service on an older agent
    /// as not known, hiding a genuine outage behind a version difference. Within that arm the
    /// boolean is the whole of what was ever known, so false is the two-valued contract's own
    /// "stopped" — carried faithfully, conflation included, rather than improved upon here.
    ///
    /// Anything else — a state a newer agent added — is not known rather than resolved through the
    /// boolean, because the boolean's relationship to a state this build has never heard of is
    /// exactly what nobody can say.
    /// </remarks>
    private static AgentServiceState ToPanelState(ServiceStatus status)
    {
        return status.State switch
        {
            ServiceState.Running => AgentServiceState.Running,
            ServiceState.Stopped => AgentServiceState.Stopped,
            ServiceState.Unknown => AgentServiceState.Unknown,
            ServiceState.Unspecified => status.Running ? AgentServiceState.Running : AgentServiceState.Stopped,
            _ => AgentServiceState.Unknown,
        };
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
