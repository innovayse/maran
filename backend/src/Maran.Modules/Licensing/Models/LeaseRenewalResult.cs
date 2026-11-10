using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Modules.Licensing.Models;

/// <summary>What one pass of the lease renewal did, in the only terms the rest of the module needs.</summary>
/// <param name="Outcome">What the pass amounted to.</param>
/// <param name="ValidUntil">The end of the lease that was stored; null unless the outcome is <see cref="LeaseRenewalOutcome.Renewed"/>.</param>
public sealed record LeaseRenewalResult(LeaseRenewalOutcome Outcome, DateTimeOffset? ValidUntil)
{
    /// <summary>A pass that stored no lease.</summary>
    /// <param name="outcome">Any outcome but <see cref="LeaseRenewalOutcome.Renewed"/>.</param>
    /// <returns>The result.</returns>
    public static LeaseRenewalResult Of(LeaseRenewalOutcome outcome)
    {
        return new LeaseRenewalResult(outcome, null);
    }
}
