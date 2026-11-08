using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FirewallService;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Services.FirewallService;

/// <summary>
/// The agent's firewall operations against the simulated server's memory: rules and bans are kept and
/// listed, and no packet is ever filtered.
/// </summary>
public sealed class SimulatedAgentFirewallClient : IAgentFirewallClient
{
    /// <summary>The rules and bans of the simulated firewall.</summary>
    private readonly SimulatedFirewallStore _firewall;

    /// <summary>The clock a timed ban starts from and a listing measures what is left of it against.</summary>
    private readonly IClock _clock;

    /// <summary>Creates a client over the shared simulated state.</summary>
    /// <param name="ledger">The simulated server's state.</param>
    /// <param name="clock">The clock timed bans are measured by.</param>
    public SimulatedAgentFirewallClient(SimulatedAgentLedger ledger, IClock clock)
    {
        _firewall = ledger.FirewallRules;
        _clock = clock;
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentFirewallRule>>> ListRulesAsync(
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_firewall.ListRules(sshPorts, panelPort));
    }

    /// <inheritdoc/>
    public Task<Result> AllowPortAsync(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_firewall.Allow(port, portTo, protocol, sourceCidr, sshPorts, panelPort));
    }

    /// <inheritdoc/>
    public Task<Result> DenyPortAsync(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_firewall.Deny(port, portTo, protocol, sourceCidr, sshPorts, panelPort));
    }

    /// <inheritdoc/>
    public Task<Result> BanAsync(string address, AgentBanTerm term, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_firewall.Ban(address, term, _clock.UtcNow));
    }

    /// <inheritdoc/>
    public Task<Result> UnbanAsync(string address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_firewall.Unban(address, _clock.UtcNow));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentFirewallBan>>> ListBansAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<IReadOnlyList<AgentFirewallBan>>.Ok(_firewall.ListBans(_clock.UtcNow)));
    }
}
