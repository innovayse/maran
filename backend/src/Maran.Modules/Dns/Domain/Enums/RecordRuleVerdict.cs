namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>The answer to "may the zone hold this record", given what it already holds.</summary>
public enum RecordRuleVerdict
{
    /// <summary>The change is allowed and was applied to the zone.</summary>
    Accepted,

    /// <summary>The zone already holds the most records an operator allows.</summary>
    ZoneFull,

    /// <summary>A CNAME cannot sit at a name that already has other data.</summary>
    CnameBesideOtherData,

    /// <summary>Other data cannot sit at a name that already has a CNAME.</summary>
    OtherDataBesideCname,

    /// <summary>A CNAME at the apex would replace the zone's own SOA and name servers.</summary>
    CnameAtApex,

    /// <summary>The apex name servers belong to the panel and cannot be edited as records.</summary>
    NsAtApex,

    /// <summary>The zone already holds this exact record.</summary>
    Duplicate,

    /// <summary>The zone holds no record with that identity.</summary>
    RecordNotFound,

    /// <summary>The record is managed by another facility and is not edited here.</summary>
    RecordNotEditable,

    /// <summary>The name already holds the most challenge values a single order needs.</summary>
    ChallengeValuesFull,
}
