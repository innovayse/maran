namespace Maran.Modules.Marketplace.Domain.Enums;

/// <summary>
/// Whether this panel's licence entitles it to a module, as the marketplace service states it. The
/// service decides; the panel only relays the state, so it holds no rule about which tier needs which
/// answer. The names are the service's wire spelling.
/// </summary>
public enum EntitlementState
{
    /// <summary>The module needs no entitlement (an Included-tier module).</summary>
    NotRequired,

    /// <summary>The licence entitles the panel to the module, until the date the entitlement carries.</summary>
    Entitled,

    /// <summary>The licence holds no entitlement to the module.</summary>
    NotPurchased,

    /// <summary>The entitlement exists but the lease that proves it has lapsed.</summary>
    LeaseExpired,

    /// <summary>The service could not tell, normally because the request carried no lease.</summary>
    Unknown,
}
