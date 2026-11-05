using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Reads the wire's service statuses into the panel's own rows.</summary>
internal static class ServiceStatusMapper
{
    /// <summary>Projects the wire statuses onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>GetServiceStatuses</c>.</param>
    /// <returns>
    /// One row per service the agent reported, in the order it sent them, or an
    /// <c>AgentInvalidResponse</c> failure when a row names a service this build has no member for —
    /// never a row standing in for a service nobody can name.
    /// </returns>
    internal static Result<IReadOnlyList<AgentServiceStatus>> ToStatusesResult(GetServiceStatusesOk ok)
    {
        var statuses = new List<AgentServiceStatus>(ok.Services.Count);

        foreach (var status in ok.Services)
        {
            var service = ToPanelService(status.Service);
            if (service is null)
            {
                return Result<IReadOnlyList<AgentServiceStatus>>.Fail(
                    Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
            }

            statuses.Add(new AgentServiceStatus(service.Value, ToPanelState(status), status.Detail));
        }

        return Result<IReadOnlyList<AgentServiceStatus>>.Ok(statuses);
    }

    /// <summary>Maps a wire service onto the panel's own.</summary>
    /// <param name="service">The value the agent sent.</param>
    /// <returns>
    /// The matching member, or <c>null</c> for the unspecified wire value or one this build has no
    /// name for.
    /// </returns>
    /// <remarks>
    /// Written out rather than cast. A cast would hand callers an enum value outside the declared
    /// set the first time a newer agent watches a unit this panel predates, and every switch over
    /// the result would then fall through its arms silently.
    /// </remarks>
    private static AgentManagedService? ToPanelService(ManagedService service)
    {
        return service switch
        {
            ManagedService.WebServer => AgentManagedService.WebServer,
            ManagedService.PhpFpm => AgentManagedService.PhpFpm,
            ManagedService.Database => AgentManagedService.Database,
            ManagedService.Ftp => AgentManagedService.Ftp,
            ManagedService.Cron => AgentManagedService.Cron,
            ManagedService.Ssh => AgentManagedService.Ssh,
            _ => null,
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
    internal static AgentServiceState ToPanelState(ServiceStatus status)
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
}
