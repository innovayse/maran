using System.Net;
using System.Net.Sockets;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// Removes an IPv6 scope id, so the panel names a caller at the resolution its firewall can act at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written from the question, not from the answer: what is the finest subject this panel can
/// BAN?</b> The agent's <c>BanAddress</c> holds a Rust <c>IpAddr</c>, and a scope id is not merely
/// dropped there — it is inexpressible: <c>"fe80::1%3".parse::&lt;IpAddr&gt;()</c> fails outright, so
/// the ban set has no way to hold the scope and no way to match on it. The finest subject the panel
/// can ban is therefore a scopeless address, and that answer decides the counting granularity
/// rather than following from it.
/// </para>
/// <para>
/// <b>Why the scope cannot be honoured even when it is true.</b> A scope id is an index into ONE
/// machine's interface table. It is meaningful only on the host that owns that index — and the
/// address the panel reads arrived through <c>X-Forwarded-For</c>, so any scope on it was minted on
/// some other machine and names nothing in this panel's namespace or the agent's. It is not a
/// distinction this panel is throwing away; it is a distinction that never carried information once
/// it crossed the proxy.
/// </para>
/// <para>
/// <b>What merging costs, stated rather than waved past.</b> <c>fe80::1%3</c> and <c>fe80::1%4</c>
/// really are two machines, and after this they share one brute-force bucket and one rate-limit
/// partition. That is the right trade because the RESPONSE cannot separate them either: the only
/// thing the panel does with a full bucket is install a scopeless ban, which blocks both scopes
/// whichever one earned it. Counting them apart would buy no finer a reaction — it would only
/// choose which of two defects to have, and the panel had the worse one. See
/// <see cref="ClientAddress"/> for the composed rule and what it replaced.
/// </para>
/// <para>
/// <b>One cost that argument does NOT cover.</b> The ban answer settles the budgets — the two login
/// rate limiters and the brute-force counter really do share one now, and it does not matter,
/// because the ban blocks both scopes whichever earned it. It settles nothing about the AUDIT
/// JOURNAL, where the address is a recorded field and not a key: two link-local machines are written
/// down under one name, and afterwards nobody can tell which of them did what. That is a forensic
/// loss, not an exhausted budget, and it is the honest price of the merge. It is judged acceptable
/// because a scoped peer cannot reach a panel installed the supported way at all — the reverse proxy
/// appends its own view of the peer and the panel adopts only that — so the journal entry that loses
/// the distinction is one no supported deployment can produce.
/// </para>
/// <para>
/// It lives beside <see cref="Ipv4MappedAddress"/> and for the same reason: <see cref="ClientAddress"/>
/// renders a connection's peer and the Firewall module's <c>IpAddressNormalizer</c> parses untrusted
/// text, and both must reach the same conclusion about the same wire fact. A second private copy is
/// a copy that can drift, and this one drifting is invisible — the address still looks like an
/// address, and the ban is simply never installed.
/// </para>
/// </remarks>
public static class ScopelessAddress
{
    /// <summary>Returns the address without its IPv6 scope id, or the address unchanged.</summary>
    /// <param name="reported">The address as it was reported or parsed.</param>
    /// <returns>The same address with no scope id; the same instance when it had none to remove.</returns>
    /// <remarks>
    /// The family is checked before <see cref="IPAddress.ScopeId"/> is read because that property
    /// THROWS on an IPv4 address rather than answering zero. Rebuilding from the raw bytes is what
    /// actually drops the scope: <see cref="IPAddress.ScopeId"/> has no setter that clears it, and
    /// the sixteen bytes of an IPv6 address never carried the scope in the first place — it is
    /// carried beside them, which is exactly why the agent's ban set cannot hold it.
    /// </remarks>
    public static IPAddress Strip(IPAddress reported)
    {
        ArgumentNullException.ThrowIfNull(reported);

        return reported.AddressFamily == AddressFamily.InterNetworkV6 && reported.ScopeId != 0
            ? new IPAddress(reported.GetAddressBytes())
            : reported;
    }
}
