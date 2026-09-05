using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;

namespace Maran.Modules.Firewall.Common;

/// <summary>Outward view of a <see cref="BanEpisode"/> that is still in force.</summary>
/// <remarks>
/// Read from the panel's own rows and not from the kernel, because the kernel cannot answer the
/// column an operator most needs: <see cref="Reason"/>. The agent's own ban listing is an address
/// and a countdown, which is enough to confirm a ban exists and nothing at all to explain it.
/// </remarks>
/// <param name="Id">The episode's identity.</param>
/// <param name="IpAddress">The banned address, in the plain form the agent holds it under.</param>
/// <param name="Reason">Why it was banned.</param>
/// <param name="Failures">How many failures the detector counted; zero for a manual ban.</param>
/// <param name="BannedAt">When the ban was placed.</param>
/// <param name="ExpiresAt">When it runs out, or null for one that lasts until somebody lifts it.</param>
public sealed record BanDto(
    Guid Id,
    string IpAddress,
    BanReason Reason,
    int Failures,
    DateTimeOffset BannedAt,
    DateTimeOffset? ExpiresAt);
