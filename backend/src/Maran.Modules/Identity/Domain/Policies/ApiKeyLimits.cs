namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The bounds on API keys: how many the panel holds, how long a name and a CIDR list may be, and how
/// often a key's last use is written. Stateless, so the entity, the create validator and the screen
/// that tells the administrator the limits all read the same numbers.
/// </summary>
public static class ApiKeyLimits
{
    /// <summary>The most keys the panel holds at once, revoked and expired ones included.</summary>
    public const int MaxKeys = 100;

    /// <summary>The longest a key's display name may be, in characters.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The most address ranges one key may be bound to.</summary>
    public const int MaxCidrs = 16;

    /// <summary>The length of a key's public prefix: lower-case hexadecimal characters.</summary>
    public const int PrefixLength = 8;

    /// <summary>The longest digest string the key row stores (a base64 or hexadecimal SHA-256).</summary>
    public const int MaxDigestLength = 64;

    /// <summary>The longest text of one stored address, which is an IPv6 address in text form.</summary>
    public const int MaxAddressLength = 45;

    /// <summary>
    /// The least time between two writes of a key's last use. A busy key would otherwise write its row
    /// on every call; the displayed last use is therefore accurate to this interval.
    /// </summary>
    public static readonly TimeSpan LastUseRecordingInterval = TimeSpan.FromMinutes(1);
}
