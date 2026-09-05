namespace Maran.Modules.Firewall.Commands.AddWhitelistEntry;

/// <summary>Adds an address range the panel's automatic bans will never touch.</summary>
/// <param name="Cidr">The range in CIDR notation — <c>203.0.113.7/32</c>, <c>2001:db8::/32</c>.</param>
/// <param name="Note">What the range is, in the administrator's own words, for whoever reads it later.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record AddWhitelistEntryCommand(string Cidr, string Note, string IpAddress, string UserAgent);
