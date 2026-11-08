using System.Buffers.Binary;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// The production <see cref="IDnsZoneSlotGate"/>: serialises one account's creations with a PostgreSQL
/// advisory lock held for the length of a transaction, counts AFTER taking it, and inserts under it.
/// </summary>
/// <remarks>
/// Two creations racing for the last slot would both read "one below the allowance" and both insert, which
/// is why the count and the insert are one step under a lock keyed by the account. The lock is
/// transaction-scoped, so a crash cannot leave it held. The count runs in its own statement after the lock:
/// READ COMMITTED gives each statement a fresh snapshot, so a request that waited here sees the winner's
/// committed row rather than the state it queued behind. Different accounts hash to different keys and never
/// wait for one another; a collision between two accounts only makes them take turns.
/// <para>
/// <b>It joins a transaction the caller already holds.</b> A creation takes its zone name's lock before the
/// zone reaches the host and keeps it until the row is committed (<see cref="IDnsZoneLock"/>), so the insert
/// here runs inside that transaction and the caller commits it; when none is open the gate opens, and commits,
/// its own. A refusal writes nothing in either case.
/// </para>
/// </remarks>
public sealed class DnsZoneSlotGate : IDnsZoneSlotGate
{
    /// <summary>The first half of the two-key advisory lock, so this module's keys cannot meet another module's.</summary>
    private const int AdvisoryLockNamespace = 0x444E5A31;

    /// <summary>The module's context; the lock, the count and the insert share its connection.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the gate over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public DnsZoneSlotGate(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<bool> TryTakeAsync(Zone zone, int allowance, CancellationToken cancellationToken)
    {
        var joined = _dbContext.Database.CurrentTransaction;
        await using var opened = joined is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // raw-sql: pg_advisory_xact_lock has no LINQ translation and no EF Core equivalent; a lock scoped to
        // a transaction is a PostgreSQL facility. Both keys are integers this method computed, passed as
        // parameters through ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy").
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock({AdvisoryLockNamespace}, {LockKeyFor(zone.AccountId)})",
            cancellationToken);

        var taken = await _dbContext.Zones
            .CountAsync(existing => existing.AccountId == zone.AccountId, cancellationToken);
        if (taken >= allowance)
        {
            if (opened is not null)
            {
                await opened.RollbackAsync(cancellationToken);
            }

            return false;
        }

        _dbContext.Zones.Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (opened is not null)
        {
            await opened.CommitAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>Derives the second advisory-lock key from an account id.</summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The first four bytes of the id as a signed integer.</returns>
    private static int LockKeyFor(Guid accountId)
    {
        Span<byte> bytes = stackalloc byte[16];
        accountId.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
}
