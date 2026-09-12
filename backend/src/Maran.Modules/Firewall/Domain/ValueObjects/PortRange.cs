using Maran.Modules.Firewall.Options;

namespace Maran.Modules.Firewall.Domain.ValueObjects;

/// <summary>
/// The one place this module decides whether a pair of numbers is a range of ports it can act on.
/// </summary>
/// <remarks>
/// <para>
/// A rule names either one port or a run of them, and the second is expressed as an upper bound
/// beside the lower one. The two ways of writing a pair that is not a range fail differently, and
/// only one of them fails loudly:
/// </para>
/// <para>
/// An INVERTED pair (<c>30099-30000</c>) is refused by <c>nft</c> itself. Because an apply is one
/// transaction, that refusal aborts the whole ruleset load: the host keeps its previous policy and
/// the administrator is told only what <c>nft</c> said. Refusing here names the actual mistake, in
/// their own language, before anything is sent.
/// </para>
/// <para>
/// An EQUAL pair (<c>30000-30000</c>) is the dangerous one, because <c>nft</c> accepts it. A
/// firewall rule has no identifier — it IS its port, its protocol and its source range — so the
/// pair would become a second spelling of the single port <c>30000</c>, and a later deny naming
/// that port would match nothing and report success while the port stayed open. One value, one
/// spelling.
/// </para>
/// <para>
/// This is advice, not the boundary. The agent refuses the same pair in its own validated type
/// before it renders a line of the ruleset (rules/architecture.md "Agent"), and a range that got
/// past this check would be refused there rather than written.
/// </para>
/// </remarks>
public static class PortRange
{
    /// <summary>Whether an upper bound is one this module will send beside <paramref name="port"/>.</summary>
    /// <param name="port">The rule's port, or the lower bound of the range.</param>
    /// <param name="portTo">The upper bound the caller asked for, or null for a single port.</param>
    /// <returns>
    /// True when there is no upper bound at all, and otherwise only when it is a usable port
    /// STRICTLY above <paramref name="port"/>.
    /// </returns>
    /// <remarks>
    /// Null answers true rather than false: a rule that names one port is the ordinary rule, and
    /// every rule written before ranges existed is one.
    /// </remarks>
    public static bool IsUsable(int port, int? portTo)
    {
        if (portTo is null)
        {
            return true;
        }

        return FirewallOptions.IsUsablePort(portTo.Value) && portTo.Value > port;
    }
}
