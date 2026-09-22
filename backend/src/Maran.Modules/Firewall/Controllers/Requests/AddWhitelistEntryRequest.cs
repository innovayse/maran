namespace Maran.Modules.Firewall.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/firewall/whitelist</c>.</summary>
/// <param name="Cidr">The range to exempt, in CIDR notation.</param>
/// <param name="Note">What the range is, in the administrator's own words, for whoever reads it later.</param>
public sealed record AddWhitelistEntryRequest(string Cidr, string Note);
