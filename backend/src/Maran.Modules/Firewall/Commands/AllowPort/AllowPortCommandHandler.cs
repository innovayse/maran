using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Resources;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Commands.AllowPort;

/// <summary>
/// Handles <see cref="AllowPortCommand"/>: refuses a rule that would lock the caller out of SSH,
/// otherwise asks the agent to open the port, and journals what happened.
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
/// <b>The lock-out refusal is enforced here, not in the browser.</b> The SPA also confirms a risky
/// allow, but a confirmation is advice: a scripted caller never sees it. Before the agent is asked to
/// install anything the handler reads the rules the host is running and asks
/// <see cref="FirewallLockoutPolicy"/> whether this allow would replace an SSH port's unconditional
/// accept with a range that excludes the address the request came from. If it would, the answer is
/// <c>FirewallRuleLocksOutCaller</c> (409), the refusal is journalled, and no allow is sent. There is
/// no override: a lost SSH path has no remote recovery, so a parameter that skips the check would be
/// the check never running. The panel's own port needs no guard, because the agent accepts it
/// unconditionally and no rule can close it (<c>ruleset_ports.rs</c>).
/// </para>
/// <para>
/// A listing that fails stops the allow with the listing's own failure: without it the handler cannot
/// tell, and it does not guess. What the guard compares is the address of this HTTP request, which is
/// the SSH client's address only when the operator uses one machine for both; the limits are in
/// docs/superpowers/notes/2026-11-07-firewall-lockout-guard-threat-note.md.
/// </para>
/// <para>
/// Nothing is stored. The firewall itself is the record of which rules exist, so a row here would be
/// a second answer to "what is open" that can disagree with the first — and the one that disagrees
/// is the one an administrator would be reading.
/// </para>
/// </remarks>
public sealed class AllowPortCommandHandler
{
    /// <summary>Pre-compiled log delegate for an allow refused because it would cut the caller off SSH.</summary>
    private static readonly Action<ILogger, string, string, int?, Exception?> LogLockoutRefused =
        LoggerMessage.Define<string, string, int?>(
            LogLevel.Warning,
            new EventId(1, nameof(AllowPortCommandHandler)),
            "Refused firewall rule {Rule}: it would stop {Caller} reaching SSH port {SshPort}");

    /// <summary>The agent, which owns everything the host's packet filter is running.</summary>
    private readonly IAgentFirewallClient _agent;

    /// <summary>The host facts every mutation carries.</summary>
    private readonly IOptions<FirewallOptions> _options;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Where the operator-facing record of a refusal goes.</summary>
    private readonly ILogger<AllowPortCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that installs the rule.</param>
    /// <param name="options">The host's SSH ports and the panel's public port.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Records which SSH port a refused allow would have closed.</param>
    public AllowPortCommandHandler(
        IAgentFirewallClient agent,
        IOptions<FirewallOptions> options,
        FirewallAuditJournal journal,
        ILogger<AllowPortCommandHandler> logger)
    {
        _agent = agent;
        _options = options;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Opens the port.</summary>
    /// <param name="command">Which port, protocol and source range to allow.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success; <c>FirewallRuleLocksOutCaller</c> when the rule would cut the caller off an SSH port;
    /// or the agent's own typed failure — <c>AgentAlreadyExists</c> for a duplicate rule.
    /// </returns>
    public async Task<Result> HandleAsync(AllowPortCommand command, CancellationToken cancellationToken)
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

        var verdict = FirewallLockoutPolicy.Judge(
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

        var allowed = await _agent.AllowPortAsync(
            command.Port,
            command.PortTo,
            command.Protocol,
            command.SourceCidr,
            options.SshPortNumbers,
            options.PanelPort,
            cancellationToken);

        if (!allowed.IsSuccess)
        {
            await RecordFailureAsync(command, subject, cancellationToken);

            return Result.Fail(allowed.Error!);
        }

        await _journal.RecordSuccessAsync(
            FirewallAuditActions.FirewallRuleAllowed,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals an allow that did not take effect, whatever stopped it.</summary>
    /// <param name="command">The command, which carries the caller's address and user agent.</param>
    /// <param name="subject">The rule, spelled the way the matching success entry spells it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    private async Task RecordFailureAsync(AllowPortCommand command, string subject, CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            FirewallAuditActions.FirewallRuleAllowed,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);
    }
}
