namespace Maran.Modules.Accounts.Domain.Enums;

/// <summary>Lifecycle state of a hosting <see cref="Account"/>.</summary>
public enum AccountStatus
{
    /// <summary>The account's sites and services are reachable.</summary>
    Active,

    /// <summary>The account is suspended: its Linux user and services are disabled by the agent.</summary>
    Suspended,
}
