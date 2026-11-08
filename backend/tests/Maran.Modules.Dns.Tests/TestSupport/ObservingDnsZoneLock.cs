using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IDnsZoneLock"/> for the in-memory provider that records each name lock taken and lets a test
/// run something at the moment the lock is granted, which is how a test stands in for the other request that
/// held it first. It serialises nothing, because that is a PostgreSQL property.
/// </summary>
public sealed class ObservingDnsZoneLock : IDnsZoneLock
{
    /// <summary>The context whose no-op transaction is opened.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Every zone name a lock was taken for, in order.</summary>
    public List<string> NamesLocked { get; } = [];

    /// <summary>Runs, with the name, once its lock is granted and before the caller continues.</summary>
    public Func<string, Task>? OnNameGranted { get; set; }

    /// <summary>Creates the stand-in over the test's context.</summary>
    /// <param name="dbContext">The test's context.</param>
    public ObservingDnsZoneLock(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> TakeAsync(Guid zoneId, CancellationToken cancellationToken)
    {
        return await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> TakeForNameAsync(string name, CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        NamesLocked.Add(name);
        if (OnNameGranted is not null)
        {
            await OnNameGranted(name);
        }

        return transaction;
    }
}
