namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>
/// Who put a record into the zone, and so who may take it out. Only <see cref="Customer"/> records are
/// edited through the zone's record endpoints; every other origin is managed by the facility that wrote
/// it, which is the seam the mail module and the certificate module publish through.
/// </summary>
public enum RecordOrigin
{
    /// <summary>The account's own record, edited by the account and by an administrator.</summary>
    Customer,

    /// <summary>A record a zone template wrote when the zone was created.</summary>
    Template,

    /// <summary>A record the panel itself keeps.</summary>
    System,

    /// <summary>A record the mail facility keeps (DKIM selector, SPF, DMARC, MX).</summary>
    Mail,

    /// <summary>A short-lived challenge record the certificate facility keeps for a DNS-01 validation.</summary>
    AcmeChallenge,
}
