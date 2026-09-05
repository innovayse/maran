namespace Maran.Modules.Firewall.Commands.UnbanAddress;

/// <summary>Lifts a ban before it runs out, or removes a permanent one.</summary>
/// <remarks>
/// Addressed by the ADDRESS rather than by an episode id, because that is the thing an operator has:
/// a customer says "I cannot reach the server from 203.0.113.7". An address may have several
/// episodes in force at once — a manual ban and an automatic one — and lifting the address lifts all
/// of them, which is the only outcome that makes the address reachable again.
/// </remarks>
/// <param name="Address">The address to let back in, in any form a client might report it.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record UnbanAddressCommand(string Address, string IpAddress, string UserAgent);
