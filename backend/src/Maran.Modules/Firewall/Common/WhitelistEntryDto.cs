using Maran.Modules.Firewall.Domain.Entities;

namespace Maran.Modules.Firewall.Common;

/// <summary>Outward view of a <see cref="WhitelistEntry"/>: one range the automatic bans never touch.</summary>
/// <param name="Id">The row's identity, and the only identifier a request may name.</param>
/// <param name="Cidr">The exempt range, exactly as it was written.</param>
/// <param name="Note">What the range is, in the administrator's own words.</param>
/// <param name="CreatedAt">When the row was added.</param>
public sealed record WhitelistEntryDto(Guid Id, string Cidr, string Note, DateTimeOffset CreatedAt);
