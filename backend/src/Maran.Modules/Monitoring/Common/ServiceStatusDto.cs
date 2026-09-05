using Maran.Agent.Client.Services.MonitorService;

namespace Maran.Modules.Monitoring.Common;

/// <summary>What the agent found out about one of the services it watches.</summary>
/// <remarks>
/// <para>
/// <b>The state is carried as its own name, not as a boolean.</b> The agent reports three values —
/// running, stopped, and not known — and the third is not decoration: on the Debian family the
/// enabled SSH unit is a socket whose service stays inactive from boot until the first connection,
/// so a two-value reading reports an outage on every such host at every reboot. Collapsing that
/// here would re-create the exact defect the agent's contract was widened to remove.
/// </para>
/// <para>
/// <b>Both names are enum members, not strings, so they serialize the way every other enum on this
/// API does.</b> The panel registers one <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c>
/// for both of its JSON surfaces, so an enum member reaches a client as <c>webServer</c> and
/// <c>running</c>. A handler that called <c>ToString()</c> and handed out the result as a plain
/// string bypassed that converter and answered <c>WebServer</c> and <c>Running</c> from the same
/// module whose <c>ChartRange</c> answers <c>lastDay</c> — one module replying in two casings, which
/// a client cannot do anything with except learn the exception. The type is what keeps the casing
/// out of a handler's hands.
/// </para>
/// <para>
/// <b>The detail is the agent's own vocabulary and is shown only to administrators</b>, which the
/// whole module already is. It names service-manager words, never a tool's standard error and never
/// anything derived from a request — no call in this path accepts a unit name.
/// </para>
/// </remarks>
/// <param name="Service">Which service this row describes, by the panel's own name for it.</param>
/// <param name="State">Up, down, or not known.</param>
/// <param name="Detail">Why, in the service manager's own words.</param>
public sealed record ServiceStatusDto(AgentManagedService Service, AgentServiceState State, string Detail);
