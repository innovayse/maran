namespace Maran.Sdk.Contracts;

/// <summary>
/// Whether a name's DNS-01 challenge can be answered through the zones this server serves, as
/// <see cref="Interfaces.IDnsChallengeRecords.CheckAsync"/> reports it.
/// </summary>
public enum DnsChallengeAvailability
{
    /// <summary>The name sits inside a zone the account owns and the name server is in service.</summary>
    Available,

    /// <summary>The name server is not switched on, or its settings are not stated, so a record would never be served.</summary>
    DnsNotEnabled,

    /// <summary>No zone the account owns contains the name, so this server cannot answer for it.</summary>
    NoZone,
}
