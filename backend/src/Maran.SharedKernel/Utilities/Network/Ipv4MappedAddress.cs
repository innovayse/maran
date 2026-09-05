using System.Net;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>
/// Unwraps the IPv4-mapped IPv6 form a dual-stack listener reports, so one machine has one address
/// everywhere in the panel.
/// </summary>
/// <remarks>
/// <para>
/// One line, and it has its own home because it is the line two different components both need and
/// both once carried privately. <see cref="ClientAddress"/> renders a connection's peer for storage
/// and counting; the Firewall module's <c>IpAddressNormalizer</c> parses untrusted text into the
/// form the agent will accept. Different inputs and different outputs — but the SAME fact about the
/// wire, and a copy of it that goes missing is invisible: the address still looks like an address,
/// the ban is still accepted by the panel, and it simply matches no packet that ever arrives.
/// </para>
/// <para>
/// <b>On scope ids the two components AGREE, and this remark used to say they did not.</b> It read
/// "different policy on scope ids", which was true when <see cref="ClientAddress"/> kept a scope and
/// the Firewall module's normaliser refused one — the disagreement that made a caller countable and
/// unbannable. The composed rule now is one rule: unwrap the mapped form here, then strip the scope
/// through <see cref="ScopelessAddress"/>, at the panel's edge and again on receipt. So a caller has
/// one spelling from the header to the agent's ban set, and this pair of helpers exists precisely so
/// that neither half can drift away from it unnoticed.
/// </para>
/// <para>
/// <c>IsIPv4MappedToIPv6</c> answers false for an address that is already IPv4, so this is safe for
/// every family without a branch on one.
/// </para>
/// </remarks>
public static class Ipv4MappedAddress
{
    /// <summary>Returns the plain IPv4 address inside a mapped one, or the address unchanged.</summary>
    /// <param name="reported">The address as it was reported or parsed.</param>
    /// <returns>Plain IPv4 when <paramref name="reported"/> was mapped; otherwise the same instance.</returns>
    public static IPAddress Unwrap(IPAddress reported)
    {
        return reported.IsIPv4MappedToIPv6 ? reported.MapToIPv4() : reported;
    }
}
