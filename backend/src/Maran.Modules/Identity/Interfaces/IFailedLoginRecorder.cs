using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Interfaces;

/// <summary>
/// Counts one failed password against a login so that every failure is counted, however many
/// arrive in the same instant, and locks the account when the count reaches the operator's threshold.
/// </summary>
/// <remarks>
/// <para>
/// This is a seam rather than a call to <see cref="User.RecordFailedLogin"/> in each handler because
/// the count is a read-modify-write on one row and <c>Users</c> carries no concurrency token: N
/// requests in flight each loaded the same count <c>k</c> and each wrote <c>k + 1</c>, so a burst of
/// guesses counted as one failure and the per-user lockout, the one control that does not depend on
/// the source address, was N times weaker than configured. The only honest implementation speaks
/// PostgreSQL (a row lock inside an explicit transaction), while most handler tests run on a provider
/// that has neither; the property itself is measured against real PostgreSQL by
/// <c>Maran.Host.IntegrationTests.ParallelFailedLoginCountTests</c>.
/// </para>
/// <para>
/// The rule itself, when the count locks the account, stays on the entity
/// (<see cref="User.RecordFailedLogin"/>); this seam only decides WHICH value of the count that rule
/// is applied to.
/// </para>
/// </remarks>
public interface IFailedLoginRecorder
{
    /// <summary>Counts a failed password against <paramref name="user"/>, serialised with every other failure for that login.</summary>
    /// <param name="user">The tracked login whose password was wrong. It is refreshed from the database before the failure is added.</param>
    /// <param name="at">The instant of the attempt, taken from the panel's clock.</param>
    /// <param name="cancellationToken">Cancels the wait for the row lock.</param>
    /// <returns>A task that completes when the new count, and any lock it caused, is committed.</returns>
    Task RecordAsync(User user, DateTimeOffset at, CancellationToken cancellationToken);
}
