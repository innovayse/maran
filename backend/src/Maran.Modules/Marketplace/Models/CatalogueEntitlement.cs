using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Models;

/// <summary>The service's statement of whether this panel's licence covers a module.</summary>
/// <param name="State">The state; an unrecognised word makes the whole answer unusable rather than guessed at.</param>
/// <param name="Until">When the entitlement ends, or null when it has no end or none applies.</param>
public sealed record CatalogueEntitlement(EntitlementState State, DateTimeOffset? Until = null);
