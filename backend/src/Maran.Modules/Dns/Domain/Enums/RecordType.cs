namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>
/// The closed set of record types a zone may hold. There is deliberately no "raw record" member: a
/// zone file is a line-oriented structured file, and a free-form record is how a caller's value becomes
/// a directive (rules/security.md item 4).
/// </summary>
public enum RecordType
{
    /// <summary>An IPv4 address.</summary>
    A,

    /// <summary>An IPv6 address.</summary>
    Aaaa,

    /// <summary>An alias to another name. Never at the apex and never beside other data at its name.</summary>
    Cname,

    /// <summary>A mail exchanger: a preference and a target host.</summary>
    Mx,

    /// <summary>Free text, restricted to printable ASCII without a quote or a backslash.</summary>
    Txt,

    /// <summary>A service locator: priority, weight, port and target host.</summary>
    Srv,

    /// <summary>A certification-authority authorisation: flags, a tag and a value.</summary>
    Caa,

    /// <summary>A delegation of a name below the apex. The apex name servers are managed by the panel.</summary>
    Ns,
}
