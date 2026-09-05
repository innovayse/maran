using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Commands.AllowPort;

/// <summary>
/// Handles <see cref="AllowPortCommand"/>: asks the agent to open a port, and journals what was
/// opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>The host facts travel with the call, and that is what keeps the server reachable.</b> The
/// agent re-renders the WHOLE ruleset on this call and the rendered policy is drop, so the SSH ports
/// and the panel port it is told about are the only reason the operator's session and the panel
/// survive an otherwise unrelated rule change. They come from <see cref="FirewallOptions"/>, which
/// the panel refuses to boot without — nothing here substitutes a value, and there is no fallback
/// 22 in this file.
/// </para>
/// <para>
/// Nothing is stored. The firewall itself is the record of which rules exist, so a row here would be
/// a second answer to "what is open" that can disagree with the first — and the one that disagrees
/// is the one an administrator would be reading.
/// </para>
/// </remarks>
public sealed class AllowPortCommandHandler
{
    /// <summary>The agent, which owns everything the host's packet filter is running.</summary>
    private readonly IAgentFirewallClient _agent;

    /// <summary>The host facts every mutation carries.</summary>
    private readonly IOptions<FirewallOptions> _options;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that installs the rule.</param>
    /// <param name="options">The host's SSH ports and the panel's public port.</param>
    /// <param name="journal">This module's audit journal.</param>
    public AllowPortCommandHandler(
        IAgentFirewallClient agent,
        IOptions<FirewallOptions> options,
        FirewallAuditJournal journal)
    {
        _agent = agent;
        _options = options;
        _journal = journal;
    }

    /// <summary>Opens the port.</summary>
    /// <param name="command">Which port, protocol and source range to allow.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or the agent's own typed failure — <c>AgentAlreadyExists</c> for a duplicate rule.</returns>
    public async Task<Result<bool>> HandleAsync(AllowPortCommand command, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var subject = FirewallRuleSubject.Describe(command.Port, command.Protocol, command.SourceCidr);

        var allowed = await _agent.AllowPortAsync(
            command.Port,
            command.Protocol,
            command.SourceCidr,
            options.SshPortNumbers,
            options.PanelPort,
            cancellationToken);

        if (!allowed.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                AuditActions.FirewallRuleAllowed,
                subject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<bool>.Fail(allowed.Error!);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.FirewallRuleAllowed,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
