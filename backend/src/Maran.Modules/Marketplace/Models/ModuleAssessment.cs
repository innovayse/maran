using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// What <c>InstallEligibility</c> decided about one catalogue module: where it stands and whether its
/// newest version can be staged now. The refusal is an error code; its sentence is produced by whoever
/// shows it.
/// </summary>
/// <param name="State">Where the module stands on this panel.</param>
/// <param name="Refusal">Why the version cannot be staged now, or null when it can.</param>
public sealed record ModuleAssessment(MarketplaceModuleState State, Error? Refusal);
