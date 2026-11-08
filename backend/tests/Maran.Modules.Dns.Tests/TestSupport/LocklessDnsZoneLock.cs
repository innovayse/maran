using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IDnsZoneLock"/> for the in-memory provider, which has no advisory locks: it opens the
/// provider's no-op transaction and takes no lock. What it cannot show is the serialisation, which is a
/// PostgreSQL property and is stated by the integration tests, not here.
/// </summary>
public sealed class LocklessDnsZoneLock : IDnsZoneLock
{
    /// <summary>The context whose transaction is opened.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Creates the stand-in over the test's context.</summary>
    /// <param name="dbContext">The test's context.</param>
    public LocklessDnsZoneLock(DnsDbContext dbContext)
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
        return await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }
}
