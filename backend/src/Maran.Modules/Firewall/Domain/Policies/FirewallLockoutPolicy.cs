using System.Net;
using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Firewall.Domain.Policies;

/// <summary>
/// Decides whether one firewall change would stop the caller reaching an SSH port: installing an
/// allow, removing a rule, or banning the caller's own address.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why only this shape of allow.</b> The agent renders a <c>policy drop</c> ruleset. The panel's
/// own port is an unconditional accept no rule can close. An SSH port is accepted unconditionally
/// only while no operator rule for it exists. An allow joins an SSH port's group only when it is TCP and names exactly
/// that ONE port (<c>ruleset_state.rs</c> <c>render</c> routes a range into an ordinary allow
/// whatever its lower bound is, and a UDP rule never displaces the accept), and a rule added to a
/// port that already has rules only widens who is admitted. What remains is one case: a TCP allow for
/// a single SSH port, whose source is neither everyone nor contains the caller, when the host holds
/// no operator rule for that port yet. The rule replaces the unconditional accept, and the caller is
/// outside what replaces it.
/// </para>
/// <para>
/// <b>A removal can close an SSH port too</b>, which an earlier version of this note denied. The
/// unconditional accept returns only when the LAST rule for the port goes; while another rule
/// remains, the port is open to exactly the remaining sources. So removing the rule that admits the
/// caller, with a rule for somebody else left behind, cuts the caller off. <see cref="JudgeRemoval"/>
/// judges the ruleset that would result: the removal is refused when rules remain for the port and
/// none of them admits the caller.
/// </para>
/// <para>
/// <b>A ban of the caller's own address closes every port at once.</b> The bans table hooks at
/// priority -5, ahead of the ruleset (priority 0), so no accept can undo a ban and the order is not
/// to be changed: a ban that a later accept could override would not be a ban.
/// <see cref="BansCaller"/> is the panel-side half of the protection.
/// </para>
/// <para>
/// "Everyone" is <c>0.0.0.0/0</c> and only that: it renders with no source clause and so also
/// covers IPv6, while <c>::/0</c> is an IPv6 restriction (<c>firewall_rule.rs</c>).
/// </para>
/// <para>
/// <b>It fails closed on an unreadable caller</b>, unlike the whitelist guard that fails open on the
/// same input. That guard protects a convenience; this one protects reachability, where a wrongly
/// refused allow costs a retry and a wrongly permitted one can cost the server. An empty or
/// unreadable address is treated as outside every restricted range.
/// </para>
/// <para>
/// What it compares is the address of the HTTP request, not of the SSH client. The two are the same
/// machine in the common case and different behind a VPN split or a jump host; the guard cannot know.
/// </para>
/// </remarks>
public static class FirewallLockoutPolicy
{
    /// <summary>The only source that renders with no source clause, so it admits every address.</summary>
    private const string EveryoneSource = "0.0.0.0/0";

    /// <summary>Judges one allow against the rules the host is running.</summary>
    /// <param name="port">The port the allow names, or the lower bound of a range.</param>
    /// <param name="portTo">The inclusive upper bound of a range, or <c>null</c> for a single port.</param>
    /// <param name="protocol">The transport protocol the allow applies to.</param>
    /// <param name="sourceCidr">The source range the allow admits.</param>
    /// <param name="existingRules">
    /// The rules the host is running, as the agent lists them. The listing leaves out the
    /// unconditional SSH accept and a lone any-source rule, which are the same thing, so no listed
    /// TCP rule for an SSH port means everyone is admitted to it today.
    /// </param>
    /// <param name="sshPorts">Every port the host's sshd listens on, from <c>FirewallOptions</c>.</param>
    /// <param name="callerAddress">The address the request arrived from; may be empty or unreadable.</param>
    /// <returns>
    /// <see cref="FirewallLockoutVerdict.Harmless"/>, or the verdict naming the SSH port the caller
    /// would stop reaching.
    /// </returns>
    public static FirewallLockoutVerdict Judge(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<AgentFirewallRule> existingRules,
        IReadOnlyList<int> sshPorts,
        string callerAddress)
    {
        // A range and a UDP rule never replace an SSH port's unconditional accept, whatever ports
        // they cover, so neither can lock anybody out.
        if (protocol != AgentFirewallProtocol.Tcp || portTo is not null || !sshPorts.Contains(port))
        {
            return FirewallLockoutVerdict.Harmless;
        }

        // A rule added next to existing rules for the port only widens who is admitted.
        var portAlreadyHasRules = existingRules.Any(rule =>
        {
            return rule.Protocol == AgentFirewallProtocol.Tcp && rule.PortTo is null && rule.Port == port;
        });

        if (portAlreadyHasRules)
        {
            return FirewallLockoutVerdict.Harmless;
        }

        if (AdmitsCaller(CidrRange.Canonical(sourceCidr), callerAddress))
        {
            return FirewallLockoutVerdict.Harmless;
        }

        return FirewallLockoutVerdict.Locks(port);
    }

    /// <summary>Judges the removal of one rule against the rules the host is running.</summary>
    /// <param name="port">The port the removed rule names, or the lower bound of a range.</param>
    /// <param name="portTo">The inclusive upper bound of a range, or <c>null</c> for a single port.</param>
    /// <param name="protocol">The transport protocol of the removed rule.</param>
    /// <param name="sourceCidr">The source range of the removed rule.</param>
    /// <param name="existingRules">The rules the host is running, as the agent lists them.</param>
    /// <param name="sshPorts">Every port the host's sshd listens on, from <c>FirewallOptions</c>.</param>
    /// <param name="callerAddress">The address the request arrived from; may be empty or unreadable.</param>
    /// <returns>
    /// <see cref="FirewallLockoutVerdict.Harmless"/>, or the verdict naming the SSH port the caller
    /// would stop reaching.
    /// </returns>
    /// <remarks>
    /// Only a TCP single-port rule for an SSH port takes part in the SSH group, so nothing else can
    /// matter. Removing a rule the host does not hold changes nothing. Removing the last rule for the
    /// port restores the unconditional accept, which admits everyone. Otherwise the port stays open to
    /// the remaining sources only, and the removal is refused unless one of them admits the caller. An
    /// unreadable caller is outside every restricted range, so it fails closed as <see cref="Judge"/>
    /// does.
    /// </remarks>
    public static FirewallLockoutVerdict JudgeRemoval(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<AgentFirewallRule> existingRules,
        IReadOnlyList<int> sshPorts,
        string callerAddress)
    {
        if (protocol != AgentFirewallProtocol.Tcp || portTo is not null || !sshPorts.Contains(port))
        {
            return FirewallLockoutVerdict.Harmless;
        }

        var removed = CidrRange.Canonical(sourceCidr);
        var portRules = existingRules
            .Where(rule =>
            {
                return rule.Protocol == AgentFirewallProtocol.Tcp && rule.PortTo is null && rule.Port == port;
            })
            .ToList();

        var held = portRules.Any(rule =>
        {
            return CidrRange.Canonical(rule.SourceCidr) == removed;
        });

        if (!held)
        {
            return FirewallLockoutVerdict.Harmless;
        }

        var remaining = portRules
            .Where(rule =>
            {
                return CidrRange.Canonical(rule.SourceCidr) != removed;
            })
            .ToList();

        // The last rule going brings the unconditional accept back: everyone is admitted again.
        if (remaining.Count == 0)
        {
            return FirewallLockoutVerdict.Harmless;
        }

        var stillAdmitted = remaining.Any(rule =>
        {
            return AdmitsCaller(CidrRange.Canonical(rule.SourceCidr), callerAddress);
        });

        return stillAdmitted ? FirewallLockoutVerdict.Harmless : FirewallLockoutVerdict.Locks(port);
    }

    /// <summary>Whether banning <paramref name="address"/> would ban the caller.</summary>
    /// <param name="address">The normalised address an administrator asked to ban.</param>
    /// <param name="callerAddress">The address the request arrived from; may be empty or unreadable.</param>
    /// <returns>True when the caller's address is the address being banned.</returns>
    /// <remarks>
    /// A ban is dropped ahead of every accept, the SSH and panel ones included, so banning the address
    /// the administrator is connected from ends the session and the way back in. An unreadable caller
    /// cannot be compared and is not refused: unlike a rule change, a ban does not shrink a range the
    /// caller might be inside, and refusing every ban from a console or an unattributed session would
    /// make the ban unusable there.
    /// </remarks>
    public static bool BansCaller(IPAddress address, string callerAddress)
    {
        return IpAddressNormalizer.TryNormalize(callerAddress, out var caller) && caller.Equals(address);
    }

    /// <summary>Whether a source range, as the allow would install it, admits the caller.</summary>
    /// <param name="sourceCidr">The canonical source range.</param>
    /// <param name="callerAddress">The request's address; may be empty or unreadable.</param>
    /// <returns>
    /// True for every source, or for a range containing a readable caller; false for an unreadable
    /// caller against any restricted range.
    /// </returns>
    private static bool AdmitsCaller(string sourceCidr, string callerAddress)
    {
        if (sourceCidr == EveryoneSource)
        {
            return true;
        }

        if (!IpAddressNormalizer.TryNormalize(callerAddress, out var caller))
        {
            return false;
        }

        return CidrRule.TryParse(sourceCidr, out var range) && range!.Contains(caller);
    }
}
