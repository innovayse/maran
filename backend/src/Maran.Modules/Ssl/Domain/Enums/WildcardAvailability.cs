namespace Maran.Modules.Ssl.Domain.Enums;

/// <summary>
/// Whether a wildcard certificate can be ordered for a domain now, and when it cannot, the one thing in
/// the way. Worked out in a fixed order so a domain has exactly one reason at a time.
/// </summary>
public enum WildcardAvailability
{
    /// <summary>The account's plan sells DNS, this server serves the domain's zone and the name server is in service.</summary>
    Available,

    /// <summary>The account's plan does not include DNS zones.</summary>
    FeatureNotIncluded,

    /// <summary>This panel was composed without the DNS module, so no record can be published at all.</summary>
    DnsModuleAbsent,

    /// <summary>The name server is not switched on, or its settings are not stated.</summary>
    DnsNotEnabled,

    /// <summary>No zone of the account's contains the domain, so this server cannot answer for it.</summary>
    ZoneNotServed,
}
