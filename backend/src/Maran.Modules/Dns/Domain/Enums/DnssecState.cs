namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>
/// Where a zone is in its DNSSEC life. The order of the values is the stored integer and never changes: a
/// state is added at the end, and <see cref="Off"/> stays zero because it is what every zone created before
/// signing existed is.
/// </summary>
public enum DnssecState
{
    /// <summary>The zone is not signed and holds no key.</summary>
    Off = 0,

    /// <summary>The zone is signed with its one key, and the registrar may hold the matching DS record.</summary>
    Signing = 1,

    /// <summary>
    /// The zone is being withdrawn from signing: the daemon follows its built-in insecure policy with the SAME
    /// key until the DS record is gone from the registrar and the minimum wait has passed.
    /// </summary>
    Withdrawing = 2,
}
