namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>The answer to "is this a name a zone may be created for".</summary>
public enum ZoneNameVerdict
{
    /// <summary>The name is a valid apex.</summary>
    Accepted,

    /// <summary>
    /// The name is not a host name of at least two labels, or its top label is all digits (which would read
    /// as an address), or it is longer than a name may be.
    /// </summary>
    Invalid,
}
