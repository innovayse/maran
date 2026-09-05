using System.Net;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Firewall.Domain.Policies;

/// <summary>
/// The one place an address entering this module is put into the form the agent accepts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Kestrel behind a dual-stack socket reports an IPv4 peer as
/// <c>::ffff:203.0.113.7</c>, and the agent's <c>BanAddress</c> REFUSES that spelling deliberately:
/// a mapped address placed in the IPv6 ban set matches no IPv4 packet that ever arrives, so
/// accepting it would install a ban that silently does nothing. Without this normalisation every
/// brute-force ban on a dual-stack host is rejected by the agent and the whole feature is inert —
/// while every unit test written against the panel's own types stays green, because nothing on this
/// side can tell the two spellings apart.
/// </para>
/// <para>
/// It is also what keeps the escalation ladder able to count. Two spellings of one address are two
/// rows, so the second offence would read as a first one and the ban would never escalate past
/// fifteen minutes.
/// </para>
/// </remarks>
public static class IpAddressNormalizer
{
    /// <summary>Parses a reported address and returns it in the form the agent accepts.</summary>
    /// <param name="reported">The address as it was reported — from a connection, or from a message.</param>
    /// <param name="normalized">
    /// The address in plain IPv4 or plain IPv6 form, or <see cref="IPAddress.None"/> when
    /// <paramref name="reported"/> is not an address at all.
    /// </param>
    /// <returns>False only when the value is not an address at all.</returns>
    /// <remarks>
    /// <para>
    /// <b>A scope id is now STRIPPED, and this method used to refuse it.</b> The refusal read
    /// correctly on its own — a ban set holds no scope, so there is nothing to install — and it was
    /// still the wrong half to hold firm, because <c>ClientAddress</c> KEPT the scope and the
    /// brute-force detector counted under the scoped string. The composed behaviour was a caller
    /// counted to the threshold, escalated, and then dropped here with "nothing was banned": a
    /// subject the panel could measure and could not answer. Refusing later cannot fix a
    /// disagreement about what an address means; only agreeing on one spelling can, and the
    /// spelling has to be the one the ban set can hold.
    /// </para>
    /// <para>
    /// So the scope goes at the panel's edge (<c>ClientAddress</c>) and again here, and this second
    /// pass is the same defence-in-depth the mapped form gets one line above — not distrust of the
    /// publisher, but the knowledge that a subscriber which ASSUMED the promise would resume
    /// dropping bans the day a publisher forgot it. What is derived is what the agent can install:
    /// <c>ScopelessAddress</c> carries the argument, including the measurement that the agent's
    /// <c>BanAddress</c> cannot parse a scoped address at all.
    /// </para>
    /// <para>
    /// <b>The strip is made on the PARSE, not on the text, and the old text check is gone.</b> That
    /// ordering is measured, not assumed: <c>IPAddress.TryParse("fe80::1%eth0")</c> succeeds and
    /// silently drops a NAMED scope, while <c>%3</c> survives the parse into
    /// <see cref="IPAddress.ScopeId"/>. Both spellings therefore arrive here scopeless once parsed
    /// and stripped, which is why one check after the parse is total where a pre-parse text check
    /// was the half that let the named spelling through. A bare <c>%3</c> or <c>203.0.113.7%3</c>
    /// fails the parse and is still refused — as not-an-address, which is what it is.
    /// </para>
    /// </remarks>
    public static bool TryNormalize(string reported, out IPAddress normalized)
    {
        if (!IPAddress.TryParse(reported, out var parsed))
        {
            normalized = IPAddress.None;
            return false;
        }

        // Both facts are the panel's, not this module's: ClientAddress needs them when it renders a
        // connection's peer, and the copy that drifts is the one nobody notices (rules/csharp.md
        // "Utilities"). The unwrap runs first by preference and not by necessity: ScopelessAddress
        // guards on the address family before it reads ScopeId, so it does not throw on IPv4, and
        // the reverse order was measured to give the same answer for ::ffff:1.2.3.4%3. An earlier
        // version of this comment asserted a crash that the guard makes impossible. This order is
        // kept because it states the pipeline: reduce to the real family, then drop the scope.
        normalized = ScopelessAddress.Strip(Ipv4MappedAddress.Unwrap(parsed));
        return true;
    }
}
