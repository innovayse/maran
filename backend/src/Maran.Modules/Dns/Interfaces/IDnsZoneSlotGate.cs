using Maran.Modules.Dns.Domain.Entities;

namespace Maran.Modules.Dns.Interfaces;

/// <summary>
/// Takes one of an account's zone slots and records the zone, or refuses because the plan's allowance is
/// already spent. A unique index refuses a repeated VALUE and the allowance is a CARDINALITY, so "at most N
/// zones for this account" cannot be an index: the count and the insert have to be one step.
/// </summary>
public interface IDnsZoneSlotGate
{
    /// <summary>Counts the account's zones and, when below the allowance, inserts <paramref name="zone"/>.</summary>
    /// <param name="zone">The zone to record. Its account is the one whose allowance is spent.</param>
    /// <param name="allowance">The most zones the account's plan allows.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>True when the zone was recorded; false when the allowance was already spent and nothing was written.</returns>
    Task<bool> TryTakeAsync(Zone zone, int allowance, CancellationToken cancellationToken);
}
