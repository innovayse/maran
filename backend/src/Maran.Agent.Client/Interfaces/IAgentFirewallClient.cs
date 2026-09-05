using Maran.Agent.Client.Services.FirewallService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the host firewall: port rules and address bans. Admin-only — the panel
/// enforces the permission, the agent enforces the syntax and renders its own nftables ruleset, and
/// no call here carries a rule string.
/// </summary>
/// <remarks>
/// Every mutation, and the listing with it, carries two host facts: the ports this host's sshd
/// listens on, and the public port the panel is reachable on. They are parameters and not settings
/// of this client, because they belong to the host rather than to the transport, and because a
/// value with a default here is a value that can be defaulted WRONG — the agent re-renders the
/// whole ruleset on every mutation under a drop policy, so a ruleset rendered without them closes
/// the operator's own session and the panel with it, and there is no remote way back in.
///
/// For the same reason nothing in this file supplies them: an empty port list or a zero port is
/// refused here and refused again by the agent, and neither side substitutes 22. The installer
/// detects the real values and writes them into <c>panel.env</c>; a value that failed to arrive is
/// a broken deployment, and the honest response to it is to change nothing.
///
/// That refusal carries its own code — <c>AgentFirewallPortsMisconfigured</c>, never the
/// <c>AgentInvalidInput</c> a bad request gets — because the two have different owners: one is
/// fixed by retyping a port, the other only by repairing the panel's own configuration. A caller
/// deciding what to tell whom needs to be able to tell them apart.
/// </remarks>
public interface IAgentFirewallClient
{
    /// <summary>Lists the port rules somebody asked for.</summary>
    /// <param name="sshPorts">
    /// Every port this host's sshd listens on. Carried by a read because the rendered ruleset's
    /// unconditional SSH and panel accepts are byte-identical to an operator's own any-source TCP
    /// allow: without these the listing would report accepts nobody created, and an administrator
    /// would then try to deny one.
    /// </param>
    /// <param name="panelPort">The public port the panel is reachable on, for the same reason.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The rules in the order the ruleset holds them — the SSH block first, then the rest — or a
    /// typed failure. The unconditional accepts the agent renders for the ports above are not among
    /// them.
    /// </returns>
    Task<Result<IReadOnlyList<AgentFirewallRule>>> ListRulesAsync(
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken);

    /// <summary>Allows traffic to a port, optionally scoped to one source range.</summary>
    /// <param name="port">The port to allow, 1-65535.</param>
    /// <param name="protocol">The transport protocol the rule applies to.</param>
    /// <param name="sourceCidr">The source range to scope the allow to; <c>0.0.0.0/0</c> allows any source.</param>
    /// <param name="sshPorts">
    /// Every port this host's sshd listens on. The agent re-renders the WHOLE ruleset on this call
    /// and the rendered policy defaults to drop, so these decide whether the operator's own session
    /// survives it. An empty list is refused with <c>AgentFirewallPortsMisconfigured</c>, nothing is
    /// sent, and nothing on the host changes.
    /// </param>
    /// <param name="panelPort">
    /// The public port the panel is reachable on — nginx's public vhost port, never whatever
    /// <c>ASPNETCORE_URLS</c> names for the backend itself (a unix socket on a server, a loopback
    /// port in development). Refused the same way when zero, for the same reason.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Success, or a typed failure — <c>AgentAlreadyExists</c> when an identical rule is already
    /// installed.
    /// </returns>
    Task<Result<bool>> AllowPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken);

    /// <summary>Removes an allow for a port, matching the source range it was scoped to.</summary>
    /// <param name="port">The port to stop allowing, 1-65535.</param>
    /// <param name="protocol">The transport protocol the rule applies to.</param>
    /// <param name="sourceCidr">The source range the original allow was scoped to.</param>
    /// <param name="sshPorts">
    /// Every port this host's sshd listens on. A deny re-renders the whole ruleset exactly as an
    /// allow does, so it can lock an operator out just as thoroughly; an empty list is refused and
    /// changes nothing.
    /// </param>
    /// <param name="panelPort">The public port the panel is reachable on, refused when zero.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure. Denying a port that is not allowed is a no-op success.</returns>
    Task<Result<bool>> DenyPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken);

    /// <summary>Bans an address: every packet from it is dropped until the ban is lifted or expires.</summary>
    /// <param name="address">The IPv4 or IPv6 address to ban.</param>
    /// <param name="ttl">
    /// How long the ban lasts, or null for a ban that lasts until somebody lifts it. The wire
    /// carries whole seconds, so a fraction is dropped: 90.7 seconds installs a 90-second ban, and
    /// never a longer one. A duration UNDER one second is refused rather than truncated, because it
    /// would arrive as the zero the contract spells "permanent"; so are a negative duration and one
    /// longer than the field can hold.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Success, or a typed failure. Banning an address that is already banned extends it to the new
    /// duration rather than failing.
    /// </returns>
    /// <remarks>
    /// No reason travels with the ban. The agent stores none, because the only place one could go on
    /// that side is an nftables comment, whose argument nft parses in its own grammar — an injection
    /// primitive for a string the panel composes. The Firewall module keeps the reason in its own
    /// row instead.
    /// </remarks>
    Task<Result<bool>> BanAsync(string address, TimeSpan? ttl, CancellationToken cancellationToken);

    /// <summary>Lifts a ban.</summary>
    /// <param name="address">The IPv4 or IPv6 address to unban.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when there was no active ban.</returns>
    Task<Result<bool>> UnbanAsync(string address, CancellationToken cancellationToken);

    /// <summary>Lists the bans currently in force.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The bans as the kernel holds them, each with the time it has left to run or null when it has
    /// no timeout, or a typed failure.
    /// </returns>
    Task<Result<IReadOnlyList<AgentFirewallBan>>> ListBansAsync(CancellationToken cancellationToken);
}
