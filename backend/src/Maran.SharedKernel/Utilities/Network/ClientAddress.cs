using System.Net;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// The one spelling of a caller's address this panel stores, counts, publishes and bans.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a type rather than <c>RemoteIpAddress.ToString()</c> at each call site.</b> Kestrel behind
/// a dual-stack socket reports an IPv4 peer as <c>::ffff:203.0.113.7</c>, and the same client
/// arriving through nginx is reported as <c>203.0.113.7</c>, because nginx writes the plain form
/// into <c>X-Forwarded-For</c>. Two spellings of one address are two rows in every per-address
/// mechanism the panel owns — every module's audit journal, the session list, the rate-limit
/// partition, and above all the brute-force counter, which would be split in half and reach its
/// threshold at twice the intended number of attempts, or never.
/// </para>
/// <para>
/// It matters a second time at the far end. The agent's <c>BanAddress</c> REFUSES the mapped
/// spelling deliberately: a mapped address placed in the IPv6 ban set matches no IPv4 packet that
/// ever arrives, so a ban built from one is a ban that silently does nothing. The Firewall module
/// normalises again on receipt — not out of distrust, but because a subscriber that assumed the
/// promise would install those inert bans the day a publisher forgot it.
/// </para>
/// <para>
/// <b>An IPv6 scope id is REMOVED, and this paragraph used to argue the opposite.</b> It said
/// <c>fe80::1%3</c> was rendered WITH its scope because the same link-local address on two
/// interfaces is two machines. That is true about the world and was still the wrong rule here, and
/// the composition is what proved it: this type kept the scope, the brute-force detector counted
/// under the scoped string, and the Firewall module's <c>IpAddressNormalizer</c> refused it — so a
/// scoped caller was counted, escalated, and could never be banned. Each of the three was
/// defensible alone; together they made one class of caller uncountable-but-unbannable.
/// </para>
/// <para>
/// The tie is broken by asking what the panel can actually DO about a caller, which is install a
/// ban — and the agent's ban set cannot hold a scope at all (<see cref="ScopelessAddress"/> carries
/// the measurement). A scope arriving through <c>X-Forwarded-For</c> is in any case an interface
/// index minted on another machine, naming nothing here. So the panel counts, limits and bans one
/// caller at the one resolution all three can express, and the losing argument's real cost —
/// <c>fe80::1%3</c> and <c>fe80::1%4</c> now share a bucket — is a cost the ban would have imposed
/// anyway, since it blocks both scopes whichever earned it.
/// </para>
/// <para>
/// <b>It lives here, and not in a module, because every module asks it.</b> Ten controllers across
/// nine modules each wrote the address into an audit command; a module may not import another
/// module's types, so each of them had spelled the answer out again — with a private
/// <c>UnknownIpAddress</c> constant apiece and without the mapped-address normalisation, which is
/// the one thing the expression exists to do.
/// </para>
/// </remarks>
public static class ClientAddress
{
    /// <summary>
    /// Recorded when the connection reports no remote address at all, as in an in-memory test host.
    /// </summary>
    /// <remarks>
    /// Deliberately not an address. It is a marker meaning "this connection had no peer", and the
    /// brute-force detector refuses to count under it precisely because it is not one: every such
    /// request would otherwise share a single bucket that names nobody and could be banned to no
    /// effect.
    /// </remarks>
    public const string Unknown = "unknown";

    /// <summary>Renders the address a connection reported, in the panel's canonical spelling.</summary>
    /// <param name="reported">The peer address as the connection reports it, or null when it has none.</param>
    /// <returns>
    /// Plain IPv4 or plain IPv6 with no scope id, or <see cref="Unknown"/> when there was no peer.
    /// </returns>
    public static string Of(IPAddress? reported)
    {
        if (reported is null)
        {
            return Unknown;
        }

        // Both facts live in SharedKernel rather than here, because the Firewall module's own
        // normaliser has to reach the identical conclusion about the identical wire fact, and a
        // second copy of either is a copy that can go missing without looking wrong.
        //
        // The unwrap runs first as a matter of reading order, NOT because the other order breaks:
        // ScopelessAddress.Strip checks the address family before it reads ScopeId, so it cannot
        // throw on IPv4, and stripping first gives the same answer for ::ffff:1.2.3.4%3 either way
        // (measured). An earlier version of this comment claimed a crash that cannot happen. The
        // reason to keep this order is that it reads as the pipeline it is — reduce the address to
        // its real family, then take the scope off what is left.
        return ScopelessAddress.Strip(Ipv4MappedAddress.Unwrap(reported)).ToString();
    }
}
