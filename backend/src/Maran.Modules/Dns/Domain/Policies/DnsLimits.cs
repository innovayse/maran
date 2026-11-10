namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The fixed bounds of a zone and its records, written once. Bounds an operator may tune live in
/// <c>DnsOptions</c>; these are the ones the protocol or the product fixes.
/// </summary>
public static class DnsLimits
{
    /// <summary>The shortest time to live accepted, in seconds. Lower values make every resolver a load generator.</summary>
    public const int MinTtl = 60;

    /// <summary>The longest time to live accepted, in seconds: one week.</summary>
    public const int MaxTtl = 604_800;

    /// <summary>The time to live a zone starts with, in seconds.</summary>
    public const int DefaultTtl = 3_600;

    /// <summary>The longest label a name may hold, in characters.</summary>
    public const int MaxLabelLength = 63;

    /// <summary>The longest full name, in characters.</summary>
    public const int MaxNameLength = 253;

    /// <summary>
    /// The longest record value, in characters. Sized so the unique index over a record's natural key stays
    /// well inside a PostgreSQL index row; a 4096-bit DKIM key is about 740 characters.
    /// </summary>
    public const int MaxValueLength = 1_024;

    /// <summary>The highest value an operator may set for the most records one zone may hold.</summary>
    public const int MaxRecordsPerZoneCeiling = 10_000;

    /// <summary>The largest preference, priority, weight or port: an unsigned 16-bit number.</summary>
    public const int MaxUnsigned16 = 65_535;

    /// <summary>The CAA flags value that marks a property critical.</summary>
    public const int CaaCriticalFlag = 128;

    /// <summary>
    /// The shortest time a withdrawal from DNSSEC signing must last before the zone may be returned to unsigned:
    /// 48 hours. It covers the longest time to live a resolver may hold the registrar's DS record, which the
    /// panel cannot see being removed, and it is enforced by the server for every caller.
    /// </summary>
    public static readonly TimeSpan DnssecWithdrawalMinimumWait = TimeSpan.FromHours(48);

    /// <summary>The CAA tags accepted.</summary>
    public static readonly IReadOnlyList<string> CaaTags = ["issue", "issuewild", "iodef"];
}
