using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FirewallService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner firewall client that records its arguments and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is as important as failing here. A retry proves the call passed through something; only a
/// call that never returns proves the something has a TIMEOUT, which is the whole reason the
/// decorator exists — a stuck unix socket must not hang the HTTP request that made the call.
///
/// It performs none of the real client's refusals. This stands in for the transport, and what the
/// decorator's tests are about is whether every argument reaches it unchanged — including the ssh
/// ports, which a decorator that dropped or reordered them would leave the real client sending a
/// ruleset the operator does not survive.
/// </remarks>
internal sealed class RecordingAgentFirewallClient : IAgentFirewallClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The rule port of the last call that named one.</summary>
    public int? LastPort { get; private set; }

    /// <summary>The protocol of the last call that named one.</summary>
    public AgentFirewallProtocol? LastProtocol { get; private set; }

    /// <summary>The source range of the last call that named one.</summary>
    public string? LastSourceCidr { get; private set; }

    /// <summary>The host's ssh ports as the last call received them.</summary>
    public IReadOnlyList<int>? LastSshPorts { get; private set; }

    /// <summary>The panel port of the last call that carried one.</summary>
    public int? LastPanelPort { get; private set; }

    /// <summary>The address of the last ban or unban.</summary>
    public string? LastAddress { get; private set; }

    /// <summary>The duration of the last ban.</summary>
    public TimeSpan? LastTtl { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentFirewallRule>>> ListRulesAsync(
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        LastSshPorts = sshPorts;
        LastPanelPort = panelPort;

        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<AgentFirewallRule>>.Ok([]);
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
        LastPort = port;
        LastProtocol = protocol;
        LastSourceCidr = sourceCidr;
        LastSshPorts = sshPorts;
        LastPanelPort = panelPort;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
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
        LastPort = port;
        LastProtocol = protocol;
        LastSourceCidr = sourceCidr;
        LastSshPorts = sshPorts;
        LastPanelPort = panelPort;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> BanAsync(string address, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        LastAddress = address;
        LastTtl = ttl;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UnbanAsync(string address, CancellationToken cancellationToken)
    {
        LastAddress = address;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentFirewallBan>>> ListBansAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<AgentFirewallBan>>.Ok([]);
    }

    /// <summary>Counts the call and applies whichever misbehaviour the test asked for.</summary>
    /// <param name="cancellationToken">The token the pipeline's timeout cancels.</param>
    /// <returns>A task that completes once the call may return.</returns>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await Task.Yield();
    }
}
