namespace Maran.Modules.Accounts.Domain.Enums;

/// <summary>Lifecycle state of a <see cref="Entities.Reseller"/>.</summary>
public enum ResellerStatus
{
    /// <summary>The reseller's login may sign in and the reseller may change what it owns.</summary>
    Active,

    /// <summary>
    /// The reseller is suspended: its login is refused and its mutations are refused. Its customers'
    /// accounts are NOT touched; an administrator suspends those separately.
    /// </summary>
    Suspended,
}
