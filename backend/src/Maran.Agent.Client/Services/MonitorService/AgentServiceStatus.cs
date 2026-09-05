namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>What the agent found out about one of the services it watches.</summary>
/// <param name="Service">Which service this row describes.</param>
/// <param name="State">Up, down, or not known.</param>
/// <param name="Detail">
/// Why, in the service manager's own vocabulary — its ActiveState and SubState words, or a short
/// sentence the agent wrote ("not yet started; ssh.socket is listening for it"). It exists because
/// <see cref="AgentServiceState.Unknown"/> deliberately collapses several situations into one, and
/// an operator needs to know which. Never a tool's standard error, and never derived from a request:
/// no call here accepts a unit name.
/// </param>
/// <remarks>
/// The wire message also carries the deprecated <c>running</c> boolean and an <c>uptime_seconds</c>
/// written as 0, and this type has a member for neither. The boolean reports both "stopped" and
/// "not known" as false — the conflation the three-valued state exists to end — and the client reads
/// it only to resolve an agent old enough to send no state at all. The uptime is unproduced: the
/// service manager reports a start timestamp rather than a duration, and turning one into the other
/// needs a clock reading nobody in this path takes.
/// </remarks>
public sealed record AgentServiceStatus(AgentManagedService Service, AgentServiceState State, string Detail);
