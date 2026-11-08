using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// Serialises everything that changes one zone, on the host and in the database, with a PostgreSQL advisory
/// lock held for the length of a transaction.
/// </summary>
/// <remarks>
/// The lock is transaction-scoped, so a crash cannot leave it held, and it is keyed by the zone, so two
/// different zones never wait for one another; a key collision between two zones only makes them take turns.
/// Whoever takes it must already have shown, through the tenant-filtered context, that the caller may see the
/// zone: a lock on an id the caller may not see would let them queue behind, and hold, someone else's edit.
/// </remarks>
public sealed class DnsZoneLock : IDnsZoneLock
{
    /// <summary>The first half of the two-key advisory lock, so this module's keys cannot meet another module's.</summary>
    private const int AdvisoryLockNamespace = 0x444E5A32;

    /// <summary>The first half of the lock keyed by a zone NAME, a different space from the one keyed by id.</summary>
    private const int NameLockNamespace = 0x444E5A33;

    /// <summary>The module's context; the lock lives on its connection.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the lock service over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public DnsZoneLock(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> TakeAsync(Guid zoneId, CancellationToken cancellationToken)
    {
        return await TakeCoreAsync(AdvisoryLockNamespace, LockKeyFor(zoneId), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> TakeForNameAsync(string name, CancellationToken cancellationToken)
    {
        return await TakeCoreAsync(NameLockNamespace, LockKeyFor(name), cancellationToken);
    }

    /// <summary>Opens a transaction and takes the two-key advisory lock inside it.</summary>
    /// <param name="lockNamespace">The first key.</param>
    /// <param name="key">The second key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The transaction; the lock is released when it is committed or disposed.</returns>
    private async Task<IDbContextTransaction> TakeCoreAsync(int lockNamespace, int key, CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // raw-sql: pg_advisory_xact_lock has no LINQ translation and no EF Core equivalent; a lock scoped to
        // a transaction is a PostgreSQL facility. Both keys are integers computed here, passed as parameters
        // through ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy").
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock({lockNamespace}, {key})",
            cancellationToken);

        return transaction;
    }

    /// <summary>Derives the second advisory-lock key from a zone name.</summary>
    /// <param name="name">The zone's apex; compared without regard to case, as zone names are.</param>
    /// <returns>The first four bytes of the name's SHA-256 as a signed integer, the same on every start.</returns>
    private static int LockKeyFor(string name)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));

        return BinaryPrimitives.ReadInt32LittleEndian(digest);
    }

    /// <summary>Derives the second advisory-lock key from a zone id.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <returns>The first four bytes of the id as a signed integer.</returns>
    private static int LockKeyFor(Guid zoneId)
    {
        Span<byte> bytes = stackalloc byte[16];
        zoneId.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
}
