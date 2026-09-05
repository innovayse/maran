using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FirewallService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent firewall operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout
/// at all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// Every method below goes through the pipeline, including the read-only ones. A listing that hangs
/// hangs a request exactly as a mutation does, and the defect this repository has already found was
/// one method quietly left undecorated while the class as a whole looked wired.
///
/// The pipeline retries transport failures, which is safe here for the reason it is safe everywhere
/// else: each of these operations is idempotent on the agent's side. A re-applied allow converges on
/// the same ruleset, and a re-applied ban extends an expiry it already set rather than stacking a
/// second one.
/// </remarks>
public sealed class ResilientAgentFirewallClient : IAgentFirewallClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentFirewallClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentFirewallClient(IAgentFirewallClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentFirewallRule>>> ListRulesAsync(
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.ListRulesAsync(state.SshPorts, state.PanelPort, token);
            },
            (Client: _inner, SshPorts: sshPorts, PanelPort: panelPort),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> AllowPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.AllowPortAsync(
                    state.Port, state.Protocol, state.SourceCidr, state.SshPorts, state.PanelPort, token);
            },
            (Client: _inner,
             Port: port,
             Protocol: protocol,
             SourceCidr: sourceCidr,
             SshPorts: sshPorts,
             PanelPort: panelPort),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DenyPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.DenyPortAsync(
                    state.Port, state.Protocol, state.SourceCidr, state.SshPorts, state.PanelPort, token);
            },
            (Client: _inner,
             Port: port,
             Protocol: protocol,
             SourceCidr: sourceCidr,
             SshPorts: sshPorts,
             PanelPort: panelPort),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> BanAsync(string address, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.BanAsync(state.Address, state.Ttl, token);
            },
            (Client: _inner, Address: address, Ttl: ttl),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UnbanAsync(string address, CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.UnbanAsync(state.Address, token);
            },
            (Client: _inner, Address: address),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentFirewallBan>>> ListBansAsync(CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.ListBansAsync(token);
            },
            _inner,
            cancellationToken);
    }
}
