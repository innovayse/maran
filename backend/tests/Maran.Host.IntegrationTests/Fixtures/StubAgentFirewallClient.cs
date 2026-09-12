using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FirewallService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent while the panel's own firewall path is exercised end to end: the real
/// controller, the real authorization policy, the real handlers, the real options binding and the
/// real problem-details translation, over real HTTP and real PostgreSQL.
/// </summary>
/// <remarks>
/// Only the agent is replaced, and only because it cannot be present: it is a separate root process
/// that edits an nftables ruleset. Everything between the HTTP request and this boundary is the
/// shipped implementation.
///
/// It is deliberately dumb — it replays a script and records what it was asked. It asserts nothing
/// itself; the tests do.
/// </remarks>
public sealed class StubAgentFirewallClient : IAgentFirewallClient
{
    /// <summary>The SSH ports the panel told the agent about on the last call, if any.</summary>
    public IReadOnlyList<int>? SshPorts { get; private set; }

    /// <summary>The panel port the panel told the agent about on the last call, if any.</summary>
    public int? PanelPort { get; private set; }

    /// <summary>
    /// The ports the last allow or deny named: the rule's port and its upper bound, which is null
    /// when the rule names a single port.
    /// </summary>
    /// <remarks>
    /// Recorded because nothing else observes it. <c>maran api</c> compares the SPA's request BODY
    /// against the command an endpoint binds and prints its own blind spot — query-string and route
    /// binding — and the removal names its rule entirely in the query string. So whether
    /// <c>?portTo=</c> actually reaches the agent is a question only a test over real HTTP can
    /// answer, and a bound dropped there would remove nothing while answering success.
    /// </remarks>
    public (int Port, int? PortTo)? RulePorts { get; private set; }

    /// <summary>Every address the panel asked to ban, in order.</summary>
    public List<string> Bans { get; } = [];

    /// <summary>Every address the panel asked to unban, in order.</summary>
    public List<string> Unbans { get; } = [];

    /// <summary>What every mutating call answers; success unless a test says otherwise.</summary>
    public Result<bool> MutationResult { get; set; } = Result<bool>.Ok(true);

    /// <summary>The rules the listing reports.</summary>
    public IReadOnlyList<AgentFirewallRule> Rules { get; set; } = [];

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentFirewallRule>>> ListRulesAsync(
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        Record(sshPorts, panelPort);
        return Task.FromResult(Result<IReadOnlyList<AgentFirewallRule>>.Ok(Rules));
    }

    /// <inheritdoc />
    public Task<Result<bool>> AllowPortAsync(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        Record(sshPorts, panelPort);
        RulePorts = (port, portTo);
        return Task.FromResult(MutationResult);
    }

    /// <inheritdoc />
    public Task<Result<bool>> DenyPortAsync(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        Record(sshPorts, panelPort);
        RulePorts = (port, portTo);
        return Task.FromResult(MutationResult);
    }

    /// <inheritdoc />
    public Task<Result<bool>> BanAsync(string address, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        Bans.Add(address);
        return Task.FromResult(MutationResult);
    }

    /// <inheritdoc />
    public Task<Result<bool>> UnbanAsync(string address, CancellationToken cancellationToken)
    {
        Unbans.Add(address);
        return Task.FromResult(MutationResult);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentFirewallBan>>> ListBansAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<IReadOnlyList<AgentFirewallBan>>.Ok([]));
    }

    /// <summary>Keeps the host facts the panel sent, which is what most of these tests are about.</summary>
    /// <param name="sshPorts">The SSH ports the call carried.</param>
    /// <param name="panelPort">The panel port the call carried.</param>
    private void Record(IReadOnlyList<int> sshPorts, int panelPort)
    {
        SshPorts = [.. sshPorts];
        PanelPort = panelPort;
    }
}
