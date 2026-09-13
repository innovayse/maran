using System.Globalization;
using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Domain.ValueObjects;

/// <summary>
/// Names one port rule in the single line the audit journal records it under.
/// </summary>
/// <remarks>
/// A rule has no identifier: it is a port, a protocol and a source range, held by the kernel and
/// not by a row here. So the journal's subject has to BE those three values, and it has to spell
/// them the same way for the allow and for the matching deny — otherwise the two entries that
/// bracket a rule's life cannot be found by one search, which is the only search an operator
/// investigating an opened port will run.
/// </remarks>
public static class FirewallRuleSubject
{
    /// <summary>
    /// Describes one rule as <c>tcp/8080 from 0.0.0.0/0</c>, or a range as
    /// <c>tcp/30000-30099 from 0.0.0.0/0</c>.
    /// </summary>
    /// <param name="port">The port the rule names, or the lower bound of a range.</param>
    /// <param name="portTo">The inclusive upper bound of a range, or null for a single port.</param>
    /// <param name="protocol">The transport protocol it applies to.</param>
    /// <param name="sourceCidr">The source range it is scoped to.</param>
    /// <returns>The journal subject.</returns>
    /// <remarks>
    /// Invariant culture, lowercase protocol: the string is a machine-searchable key in an
    /// append-only journal, so it must not change with the reader's language.
    ///
    /// A range is written with both bounds, and a single port with neither separator nor a repeated
    /// number — so the entry for <c>tcp/30000</c> and the entry for <c>tcp/30000-30099</c> are two
    /// different searches, which they have to be, because they are two different rules. An entry
    /// that recorded only the lower bound would say a port was opened while ninety-nine others were
    /// opened beside it.
    /// </remarks>
    public static string Describe(int port, int? portTo, AgentFirewallProtocol protocol, string sourceCidr)
    {
        var name = protocol == AgentFirewallProtocol.Udp ? "udp" : "tcp";
        var ports = portTo is null
            ? string.Create(CultureInfo.InvariantCulture, $"{port}")
            : string.Create(CultureInfo.InvariantCulture, $"{port}-{portTo.Value}");

        return string.Create(CultureInfo.InvariantCulture, $"{name}/{ports} from {sourceCidr}");
    }
}
