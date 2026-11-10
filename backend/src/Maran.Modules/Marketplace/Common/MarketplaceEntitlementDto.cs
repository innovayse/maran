using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Common;

/// <summary>Whether this panel's licence entitles it to a module.</summary>
/// <param name="State">The state, as a camelCase word the screen branches on.</param>
/// <param name="StateDisplayName">The state in the caller's language.</param>
/// <param name="Until">When the entitlement ends, or null when it has no end or none is known.</param>
public sealed record MarketplaceEntitlementDto(EntitlementState State, string StateDisplayName, DateTimeOffset? Until);
