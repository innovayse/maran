namespace Maran.Modules.Firewall.Commands.BanAddress;

/// <summary>
/// Bans an address from the host at an administrator's request: every packet from it is dropped
/// until the ban is lifted or runs out.
/// </summary>
/// <remarks>
/// A manual ban records an episode of its own, exactly as an automatic one does. Without the row the
/// ban would not survive the next reboot — the agent keeps no ban state and both families' nftables
/// units flush on stop — so an administrator's deliberate ban would be the one kind that quietly
/// stops being in force.
/// </remarks>
/// <param name="Address">The address to ban, in any form a client might report it.</param>
/// <param name="DurationMinutes">
/// How long the ban lasts, or <c>null</c> for one that lasts until somebody lifts it. Minutes rather
/// than seconds because that is the unit an administrator thinks in, and the shortest ban the
/// contract can express is a second.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record BanAddressCommand(
    string Address,
    int? DurationMinutes,
    string IpAddress,
    string UserAgent);
