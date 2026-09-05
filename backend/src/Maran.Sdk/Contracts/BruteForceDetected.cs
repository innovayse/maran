namespace Maran.Sdk.Contracts;

/// <summary>
/// Announced when repeated failed sign-ins from one address cross the panel's brute-force
/// threshold. The Identity module publishes it; the Firewall module bans the address.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a contract in the Sdk.</b> The module that COUNTS failures owns sign-in, and the module
/// that BANS owns the host firewall, and neither may reference the other (rules/architecture.md
/// "Backend: modular monolith", enforced by <c>ModuleIsolationTests</c>). So the message is declared
/// here, in the surface both already depend on.
/// </para>
/// <para>
/// <b>The address is already normalised when it arrives.</b> The publisher maps
/// <c>::ffff:a.b.c.d</c> onto plain IPv4 and drops any IPv6 scope id at the point it reads the peer,
/// and the subscriber normalises again on receipt — not out of distrust but because a subscriber
/// that assumed the promise would install bans that silently match nothing the day a publisher
/// forgot it, and there is no observable difference between "banned" and "banned in a form no
/// packet carries".
/// </para>
/// <para>
/// <b>The scope id half of that was once a disagreement rather than a promise, and it cost a
/// bypass-shaped defect.</b> The publisher kept the scope and the subscriber refused it, so a
/// scoped caller was counted here, escalated, and never banned. Both ends now spell the address the
/// way the agent's ban set can hold it — scopeless — because that set cannot express a scope at
/// all, which makes it the finest subject this message can usefully name.
/// </para>
/// <para>
/// <b>It carries no threshold and no duration.</b> How many failures count as an attack is the
/// detector's policy; how long the resulting ban lasts is the firewall's, and it escalates with how
/// often the same address has been seen. Putting either on the wire would give one module a vote in
/// the other's decision and two places for the answer to be changed.
/// </para>
/// </remarks>
/// <param name="IpAddress">
/// The address the failures came from, as plain IPv4 or plain IPv6 — never the IPv4-mapped IPv6
/// spelling a dual-stack listener reports, and never carrying an IPv6 scope id. The agent refuses
/// both.
/// </param>
/// <param name="Failures">How many failed attempts were counted inside the window.</param>
/// <param name="WindowStart">
/// The start of the counting window. Paired with <paramref name="IpAddress"/> it is the message's
/// identity: a durable queue may deliver the same detection twice, and the second delivery must not
/// extend the ban or count as a second offence.
/// </param>
public sealed record BruteForceDetected(string IpAddress, int Failures, DateTimeOffset WindowStart);
