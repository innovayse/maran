namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for DNS actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record. Every entry names the zone or the record it took effect on and NEVER a
/// record's value: a TXT value may carry a key or a token (rules/security.md item 8).
/// </remarks>
public static class DnsAuditActions
{
    /// <summary>The server's authoritative DNS daemon was configured and started.</summary>
    public const string DnsEnabled = "DnsEnabled";

    /// <summary>The server's authoritative DNS daemon was stopped.</summary>
    public const string DnsDisabled = "DnsDisabled";

    /// <summary>The server's name-server settings (addresses, name-server host names, hostmaster) were saved.</summary>
    public const string DnsSettingsSaved = "DnsSettingsSaved";

    /// <summary>A zone was created and handed to the daemon.</summary>
    public const string DnsZoneCreated = "DnsZoneCreated";

    /// <summary>A zone was removed from the daemon and from the panel.</summary>
    public const string DnsZoneDeleted = "DnsZoneDeleted";

    /// <summary>A record was added to a zone.</summary>
    public const string DnsRecordAdded = "DnsRecordAdded";

    /// <summary>A record of a zone was changed.</summary>
    public const string DnsRecordChanged = "DnsRecordChanged";

    /// <summary>A record was removed from a zone.</summary>
    public const string DnsRecordDeleted = "DnsRecordDeleted";

    /// <summary>A zone's whole record set was replaced in one step (an import).</summary>
    public const string DnsRecordsReplaced = "DnsRecordsReplaced";

    /// <summary>A zone template was created or replaced. The entry names the template, never its records.</summary>
    public const string ZoneTemplateSaved = "ZoneTemplateSaved";

    /// <summary>A zone template was deleted.</summary>
    public const string ZoneTemplateDeleted = "ZoneTemplateDeleted";

    /// <summary>The template new sites get their zone from was changed.</summary>
    public const string ZoneTemplateDefaultChanged = "ZoneTemplateDefaultChanged";

    /// <summary>DNSSEC signing was switched on for a zone. The entry names the zone and never the key.</summary>
    public const string ZoneSigningEnabled = "ZoneSigningEnabled";

    /// <summary>A zone's withdrawal from DNSSEC signing began. The entry names the zone and never the key.</summary>
    public const string ZoneSigningWithdrawalStarted = "ZoneSigningWithdrawalStarted";

    /// <summary>A zone's withdrawal from DNSSEC signing ended: the zone is unsigned and its key is deleted.</summary>
    public const string ZoneSigningWithdrawn = "ZoneSigningWithdrawn";
}
