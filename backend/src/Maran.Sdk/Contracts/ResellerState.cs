namespace Maran.Sdk.Contracts;

/// <summary>
/// Where a reseller stands in its lifecycle, as another module may read it through
/// <see cref="ResellerSnapshot"/>. Mirrors the Accounts module's own status, which no other module
/// may reference (rules/architecture.md).
/// </summary>
public enum ResellerState
{
    /// <summary>The reseller is in service: its login can sign in and its mutations are accepted.</summary>
    Active,

    /// <summary>The reseller is suspended: its login is refused and its mutations are not accepted.</summary>
    Suspended,
}
