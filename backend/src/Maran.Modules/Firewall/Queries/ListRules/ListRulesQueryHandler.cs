using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Queries.ListRules;

/// <summary>
/// Handles <see cref="ListRulesQuery"/> by asking the agent what the firewall is actually running.
/// </summary>
/// <remarks>
/// <para>
/// <b>The read carries the host facts too, and that is not symmetry for its own sake.</b> The
/// unconditional accepts the agent renders for the host's SSH ports and the panel's own port are
/// byte-identical to an operator's own any-source TCP allow, so without those ports the listing
/// would report accepts nobody created — and an administrator would then try to deny one, which is
/// how a listing becomes a lockout.
/// </para>
/// <para>
/// There is no row for a rule anywhere in this module. The firewall IS the record of what is open,
/// so a cached copy here would be a second answer able to disagree with the machine — and the
/// disagreement would only ever be discovered by somebody acting on the wrong one.
/// </para>
/// </remarks>
public sealed class ListRulesQueryHandler
{
    /// <summary>The agent, which owns everything the host's packet filter is running.</summary>
    private readonly IAgentFirewallClient _agent;

    /// <summary>The host facts the listing needs in order to leave the ruleset's own accepts out.</summary>
    private readonly IOptions<FirewallOptions> _options;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the ruleset.</param>
    /// <param name="options">The host's SSH ports and the panel's public port.</param>
    public ListRulesQueryHandler(IAgentFirewallClient agent, IOptions<FirewallOptions> options)
    {
        _agent = agent;
        _options = options;
    }

    /// <summary>Returns the installed rules, in the order the ruleset holds them.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rules, or the agent's own typed failure.</returns>
    public async Task<Result<IReadOnlyList<FirewallRuleDto>>> HandleAsync(
        ListRulesQuery query,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;

        var rules = await _agent.ListRulesAsync(options.SshPortNumbers, options.PanelPort, cancellationToken);
        if (!rules.IsSuccess)
        {
            return Result<IReadOnlyList<FirewallRuleDto>>.Fail(rules.Error!);
        }

        var projected = rules.Value
            .Select(rule =>
            {
                return new FirewallRuleDto(rule.Port, rule.Protocol, rule.SourceCidr);
            })
            .ToList();

        return Result<IReadOnlyList<FirewallRuleDto>>.Ok(projected);
    }
}
