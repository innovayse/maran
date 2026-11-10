namespace Maran.Sdk.Contracts;

/// <summary>
/// The receipt for one challenge record a facility published, which is the only thing it can ask to have
/// removed. It names the account, the zone and the record so that a removal can be scoped to the account
/// that published and can touch nothing but a challenge record.
/// </summary>
/// <param name="AccountId">The account whose zone holds the record.</param>
/// <param name="ZoneId">The zone that holds the record.</param>
/// <param name="RecordId">The record, as the DNS module knows it.</param>
public sealed record DnsChallengeHandle(Guid AccountId, Guid ZoneId, Guid RecordId);
