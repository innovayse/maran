using Maran.Modules.Databases.Domain.Entities;

namespace Maran.Modules.Databases.Interfaces;

/// <summary>
/// The last word on whether an account may hold one more additional database user, taken at the moment
/// the row is written rather than before the server is touched.
/// </summary>
/// <remarks>
/// The same defect and the same answer as <see cref="IDatabaseSlotGate"/>: "at most N rows for this
/// account" is a cardinality no key can express, so the count and the insert are made one step under a
/// per-account advisory lock, and the gate is a seam because its only honest implementation speaks
/// PostgreSQL. A caller reaches it AFTER the agent has created the login, so a refusal means a login
/// exists that no row will own and the caller compensates.
/// </remarks>
public interface IDatabaseUserLimitGate
{
    /// <summary>Writes the row if, and only if, the account is still inside its allowance of additional users.</summary>
    /// <param name="user">The row to write, for a login the agent has already created.</param>
    /// <param name="allowance">How many additional users the account may hold.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>True when the row was committed; false when a concurrent creation took the last slot, and nothing was written.</returns>
    Task<bool> TryTakeAsync(DatabaseUser user, int allowance, CancellationToken cancellationToken);
}
