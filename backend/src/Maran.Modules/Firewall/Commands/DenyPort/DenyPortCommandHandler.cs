using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Resources;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Commands.DenyPort;

/// <summary>
/// Handles <see cref="DenyPortCommand"/>: refuses a removal that would leave the caller outside every
/// remaining rule of an SSH port, otherwise asks the agent to remove the allow, and journals what was
/// closed.
/// </summary>
/// <remarks>
/// <para>
/// <b>A removal can close an SSH port.</b> The agent restores the unconditional SSH accept only when
/// the LAST rule for the port goes. While another rule remains, the port admits only the remaining
/// sources, so removing the rule that admits the caller cuts the caller off. Before the agent is
/// asked, the handler reads the rules the host is running and asks
/// <see cref="FirewallLockoutPolicy.JudgeRemoval"/> about the ruleset that would result. A refusal is
/// <c>FirewallRuleLocksOutCaller</c> (409), journalled, with nothing sent. A listing that fails stops
/// the removal with the listing's own failure: the handler cannot tell, and does not guess. Two
/// administrators changing the same port within one request can still defeat the check (the listing
/// and the removal are two agent calls).
/// </para>
/// <para>
/// A deny carries the host facts for exactly the same reason an allow does, and the reason is worth
/// stating on both: the agent re-renders the WHOLE ruleset here too, so closing an unrelated port
/// can lock the operator out just as thoroughly as opening one. There is no path through this module
/// that reaches the agent without <see cref="FirewallOptions"/>.
/// </para>
/// </remarks>
public sealed class DenyPortCommandHandler
{
    /// <summary>The agent, which owns everything the host's packet filter is running.</summary>
    private readonly IAgentFirewallClient _agent;

    /// <summary>The host facts every mutation carries.</summary>
    private readonly IOptions<FirewallOptions> _options;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Where the operator-facing record of a refusal goes.</summary>
    private readonly ILogger<DenyPortCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that removes the rule.</param>
    /// <param name="options">The host's SSH ports and the panel's public port.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Records which SSH port a refused removal would have closed.</param>
    public DenyPortCommandHandler(
        IAgentFirewallClient agent,
        IOptions<FirewallOptions> options,
        FirewallAuditJournal journal,
        ILogger<DenyPortCommandHandler> logger)
    {
        _logger = logger;
        _agent = agent;
        _options = options;
        _journal = journal;
    }

    /// <summary>Pre-compiled log delegate for a removal refused because it would cut the caller off SSH.</summary>
    private static readonly Action<ILogger, string, string, int?, Exception?> LogLockoutRefused =
        LoggerMessage.Define<string, string, int?>(
            LogLevel.Warning,
            new EventId(1, nameof(DenyPortCommandHandler)),
            "Refused removal of firewall rule {Rule}: it would stop {Caller} reaching SSH port {SshPort}");

    /// <summary>Closes the port.</summary>
    /// <param name="command">Which port, protocol and source range to stop allowing.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success — denying a port that is not allowed is a no-op success; <c>FirewallRuleLocksOutCaller</c>
    /// when the removal would cut the caller off an SSH port; or the agent's typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(DenyPortCommand command, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var subject = FirewallRuleSubject.Describe(
            command.Port,
            command.PortTo,
            command.Protocol,
            command.SourceCidr);

        var listed = await _agent.ListRulesAsync(options.SshPortNumbers, options.PanelPort, cancellationToken);
        if (!listed.IsSuccess)
        {
            await RecordFailureAsync(command, subject, cancellationToken);

            return Result.Fail(listed.Error!);
        }

        var verdict = FirewallLockoutPolicy.JudgeRemoval(
            command.Port,
            command.PortTo,
            command.Protocol,
            command.SourceCidr,
            listed.Value,
            options.SshPortNumbers,
            command.IpAddress);

        if (verdict.LocksOutCaller)
        {
            LogLockoutRefused(_logger, subject, command.IpAddress, verdict.LostSshPort, null);

            await RecordFailureAsync(command, subject, cancellationToken);

            return Result.Fail(Error.Of(nameof(ErrorMessages.FirewallRuleLocksOutCaller), ErrorType.Conflict));
        }

        var denied = await _agent.DenyPortAsync(
            command.Port,
            command.PortTo,
            command.Protocol,
            command.SourceCidr,
            options.SshPortNumbers,
            options.PanelPort,
            cancellationToken);

        if (!denied.IsSuccess)
        {
            await RecordFailureAsync(command, subject, cancellationToken);

            return Result.Fail(denied.Error!);
        }

        await _journal.RecordSuccessAsync(
            FirewallAuditActions.FirewallRuleDenied,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a removal that did not take effect, whatever stopped it.</summary>
    /// <param name="command">The command, which carries the caller's address and user agent.</param>
    /// <param name="subject">The rule, spelled the way the matching success entry spells it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    private async Task RecordFailureAsync(DenyPortCommand command, string subject, CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            FirewallAuditActions.FirewallRuleDenied,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }
}
