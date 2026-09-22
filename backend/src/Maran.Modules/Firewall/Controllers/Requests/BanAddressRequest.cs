namespace Maran.Modules.Firewall.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/firewall/bans</c>.</summary>
/// <param name="Address">The address to ban.</param>
/// <param name="DurationMinutes">
/// How long the ban lasts, or <c>null</c> for one that lasts until somebody lifts it. Absent means
/// permanent on purpose: a permanent ban should be something the caller chose by omission rather
/// than something a zero produced by accident.
/// </param>
public sealed record BanAddressRequest(string Address, int? DurationMinutes);
