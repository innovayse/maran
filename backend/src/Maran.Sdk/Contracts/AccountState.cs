namespace Maran.Sdk.Contracts;

/// <summary>
/// Where a hosting account stands in its lifecycle, as another module may read it through
/// <see cref="AccountSnapshot"/>. Mirrors the Accounts module's own status, which no other module
/// may reference (rules/architecture.md).
/// </summary>
public enum AccountState
{
    /// <summary>The account is in service: its sites serve and its login can sign in.</summary>
    Active,

    /// <summary>The account is suspended: its sites are stopped and its owner is offered no way in.</summary>
    Suspended,
}
