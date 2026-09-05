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
    /// <summary>Describes one rule as <c>tcp/8080 from 0.0.0.0/0</c>.</summary>
    /// <param name="port">The port the rule names.</param>
    /// <param name="protocol">The transport protocol it applies to.</param>
    /// <param name="sourceCidr">The source range it is scoped to.</param>
    /// <returns>The journal subject.</returns>
    /// <remarks>
    /// Invariant culture, lowercase protocol: the string is a machine-searchable key in an
    /// append-only journal, so it must not change with the reader's language.
    /// </remarks>
    public static string Describe(int port, AgentFirewallProtocol protocol, string sourceCidr)
    {
        var name = protocol == AgentFirewallProtocol.Udp ? "udp" : "tcp";
        return string.Create(CultureInfo.InvariantCulture, $"{name}/{port} from {sourceCidr}");
    }
}
