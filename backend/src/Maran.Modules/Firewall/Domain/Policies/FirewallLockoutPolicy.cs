using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Firewall.Domain.Policies;

/// <summary>
/// Decides whether installing one allow would stop the caller reaching an SSH port, which is the one
/// firewall mutation that can cost an operator their way into the server.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why only this shape of allow.</b> The agent renders a <c>policy drop</c> ruleset. The panel's
/// own port is an unconditional accept no rule can close. An SSH port is accepted unconditionally
/// only while no operator rule for it exists, and a removal brings that accept back, so a removal
/// never closes anything. An allow joins an SSH port's group only when it is TCP and names exactly
/// that ONE port (<c>ruleset_state.rs</c> <c>render</c> routes a range into an ordinary allow
/// whatever its lower bound is, and a UDP rule never displaces the accept), and a rule added to a
/// port that already has rules only widens who is admitted. What remains is one case: a TCP allow for
/// a single SSH port, whose source is neither everyone nor contains the caller, when the host holds
/// no operator rule for that port yet. The rule replaces the unconditional accept, and the caller is
/// outside what replaces it.
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
