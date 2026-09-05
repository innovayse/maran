using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Commands.DenyPort;

/// <summary>
/// Handles <see cref="DenyPortCommand"/>: asks the agent to remove an allow, and journals what was
/// closed.
/// </summary>
/// <remarks>
/// A deny carries the host facts for exactly the same reason an allow does, and the reason is worth
/// stating on both: the agent re-renders the WHOLE ruleset here too, so closing an unrelated port
/// can lock the operator out just as thoroughly as opening one. There is no path through this module
/// that reaches the agent without <see cref="FirewallOptions"/>.
/// </remarks>
public sealed class DenyPortCommandHandler
{
    /// <summary>The agent, which owns everything the host's packet filter is running.</summary>
    private readonly IAgentFirewallClient _agent;

    /// <summary>The host facts every mutation carries.</summary>
    private readonly IOptions<FirewallOptions> _options;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that removes the rule.</param>
    /// <param name="options">The host's SSH ports and the panel's public port.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DenyPortCommandHandler(
        IAgentFirewallClient agent,
        IOptions<FirewallOptions> options,
        FirewallAuditJournal journal)
    {
        _agent = agent;
        _options = options;
        _journal = journal;
    }

    /// <summary>Closes the port.</summary>
    /// <param name="command">Which port, protocol and source range to stop allowing.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success — denying a port that is not allowed is a no-op success — or the agent's typed failure.</returns>
    public async Task<Result<bool>> HandleAsync(DenyPortCommand command, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var subject = FirewallRuleSubject.Describe(command.Port, command.Protocol, command.SourceCidr);

        var denied = await _agent.DenyPortAsync(
            command.Port,
            command.Protocol,
            command.SourceCidr,
            options.SshPortNumbers,
            options.PanelPort,
            cancellationToken);

        if (!denied.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                AuditActions.FirewallRuleDenied,
                subject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<bool>.Fail(denied.Error!);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.FirewallRuleDenied,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
