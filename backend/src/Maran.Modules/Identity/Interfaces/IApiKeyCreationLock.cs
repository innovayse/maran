using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Identity.Interfaces;

/// <summary>
/// Opens the transaction an API key is created in and makes it the only key creation in the panel at
/// that moment, so the panel-wide key limit and the prefix-collision check behind it cannot be
/// interleaved by a second creation.
/// </summary>
/// <remarks>
/// <para>
/// This is a seam for the reason <see cref="ISubUserTeamLock"/> is: its only honest implementation
/// speaks PostgreSQL, while most of the handler's tests run on a provider that has no advisory locks.
/// </para>
/// <para>
/// <b>Why an advisory lock and not a row lock.</b> The limit is a panel-wide cardinality and there is
/// no row that stands for "the panel's keys" to lock. READ COMMITTED gives each statement its own
/// snapshot, so two creations that both counted one below the limit both insert; the lock makes the
/// second creation wait, and its count then sees the first one's committed row.
/// </para>
/// </remarks>
public interface IApiKeyCreationLock
{
    /// <summary>Begins a transaction and takes the panel's key-creation lock inside it.</summary>
    /// <param name="cancellationToken">Cancels the wait for the lock.</param>
    /// <returns>
    /// The open transaction. The lock is held until it is committed or rolled back (disposing it rolls
    /// it back), and it covers every statement the caller runs on the module's context meanwhile.
    /// </returns>
    Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken);
}
