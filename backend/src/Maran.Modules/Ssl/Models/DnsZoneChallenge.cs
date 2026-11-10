namespace Maran.Modules.Ssl.Models;

/// <summary>
/// A wildcard order's challenge: a TXT record published into a DNS zone this server serves for the account
/// (DNS-01), and removed again once the authority has read it.
/// </summary>
/// <remarks>
/// It carries the account and nothing else. The record is placed through the DNS module's challenge door
/// (<c>IDnsChallengeRecords</c>), which finds the account's zone itself and can write only a challenge, so
/// no path, zone name or record text is named here for anyone to widen.
/// </remarks>
/// <param name="AccountId">The account whose zone answers.</param>
public sealed record DnsZoneChallenge(Guid AccountId) : AcmeChallengeTarget;
