namespace Maran.Agent.Client.Services.DnsService;

/// <summary>The closed set of record types the agent accepts. There is no "raw record" kind.</summary>
public enum DnsRecordKind
{
    /// <summary>An IPv4 address.</summary>
    A,

    /// <summary>An IPv6 address.</summary>
    Aaaa,

    /// <summary>An alias to another name.</summary>
    Cname,

    /// <summary>A mail exchanger with a preference.</summary>
    Mx,

    /// <summary>Text.</summary>
    Txt,

    /// <summary>A service locator: priority, weight, port and target.</summary>
    Srv,

    /// <summary>A certification-authority authorisation: flags, tag and value.</summary>
    Caa,

    /// <summary>A delegation to a name server below the apex.</summary>
    Ns,
}
