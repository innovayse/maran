using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Dns.Interfaces;

/// <summary>
/// Serialises everything that changes one zone, on the host and in the database. Whoever takes it must
/// already have shown, through the tenant-filtered context, that the caller may see the zone: a lock on an
/// id the caller may not see would let them queue behind, and hold, someone else's edit.
/// </summary>
/// <remarks>
/// Two keys, because a zone has two lives. An EXISTING zone is serialised by its id (<c>TakeAsync</c>); a zone
/// that has no row yet, or whose row is being looked for, can only be named by its apex (<c>TakeForNameAsync</c>).
/// The creation of a name and the removal of a served name nobody owns take the SAME name lock, which is what
/// keeps the start-up pass from removing a zone another request has put on the host and is about to record.
/// </remarks>
public interface IDnsZoneLock
{
    /// <summary>Opens a transaction and takes the zone's lock inside it.</summary>
    /// <param name="zoneId">The zone to serialise on.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction; the lock is released when it is committed or disposed.</returns>
    Task<IDbContextTransaction> TakeAsync(Guid zoneId, CancellationToken cancellationToken);

    /// <summary>Opens a transaction and takes the lock of a zone NAME inside it, whether or not a row has that name yet.</summary>
    /// <param name="name">The zone's normalised apex.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction; the lock is released when it is committed or disposed.</returns>
    Task<IDbContextTransaction> TakeForNameAsync(string name, CancellationToken cancellationToken);
}
