namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>
/// The two raw values this server's licence fingerprint is derived from, as the agent read them.
/// </summary>
/// <param name="MachineId">
/// This host's machine-id, or <see langword="null"/> when the host has none.
/// </param>
/// <param name="PrimaryInterface">
/// The interface carrying this host's IPv4 default route, or <see langword="null"/> when there is
/// no such route.
/// </param>
/// <remarks>
/// <para>
/// <b>Null rather than an empty string, in both positions.</b> The wire carries a present/value
/// pair precisely so that "there is none" is a state and not a value; collapsing it to an empty
/// string here would throw that distinction away one layer below where it was made, and leave the
/// binding policy comparing a licence against <c>""</c>.
/// </para>
/// <para>
/// <b>Neither value is ever logged.</b> Together they identify one machine for as long as it
/// exists (rules/security.md item 8), and a licence refusal that printed them would put a stable
/// server identity into a log file that is routinely shipped elsewhere.
/// </para>
/// </remarks>
public sealed record AgentServerFingerprint(string? MachineId, string? PrimaryInterface);
