namespace Maran.Modules.Firewall.Commands.RemoveWhitelistEntry;

/// <summary>Removes an address range from the whitelist, so the automatic bans may reach it again.</summary>
/// <param name="EntryId">Which row to remove.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record RemoveWhitelistEntryCommand(Guid EntryId, string IpAddress, string UserAgent);
