using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Sftp.Interfaces;

/// <summary>
/// Serialises the key changes of one SFTP login by locking the login's row for the length of a
/// transaction.
/// </summary>
/// <remarks>
/// The panel sends the agent the COMPLETE key set after every change, so two simultaneous changes on
/// one login would each read the set, each add or drop their own key, and each send a set lacking the
/// other's: the last write wins at the agent while both rows commit. Holding this lock from before
/// the set is read until the commit makes the second change read the first one's committed rows.
/// </remarks>
public interface ISftpUserKeyLock
{
    /// <summary>Opens a transaction and takes the login's row lock inside it.</summary>
    /// <param name="sftpUserId">The login whose key changes are being serialised.</param>
    /// <param name="cancellationToken">Cancels the wait for the lock.</param>
    /// <returns>
    /// The open transaction. The lock is released by its commit or rollback, so a caller that
    /// disposes it without committing has rolled everything back.
    /// </returns>
    Task<IDbContextTransaction> BeginAsync(Guid sftpUserId, CancellationToken cancellationToken);
}
