using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IDnsZoneLock"/> for the in-memory provider that counts the locks currently held, which is how
/// a test states that a lock is free at a moment: between the publish of a challenge record and its removal,
/// when the certificate authority is validating. It serialises nothing, because that is a PostgreSQL property.
/// </summary>
public sealed class CountingDnsZoneLock : IDnsZoneLock
{
    /// <summary>The context whose no-op transaction is opened.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>How many locks are taken and not yet released.</summary>
    public int Held { get; private set; }

    /// <summary>Creates the stand-in over the test's context.</summary>
    /// <param name="dbContext">The test's context.</param>
    public CountingDnsZoneLock(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> TakeAsync(Guid zoneId, CancellationToken cancellationToken)
    {
        return await TakeCountedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> TakeForNameAsync(string name, CancellationToken cancellationToken)
    {
        return await TakeCountedAsync(cancellationToken);
    }

    /// <summary>Opens the transaction and counts it until it is released.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The counted transaction.</returns>
    private async Task<IDbContextTransaction> TakeCountedAsync(CancellationToken cancellationToken)
    {
        var inner = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        Held++;

        return new CountedTransaction(inner, () =>
        {
            Held--;
        });
    }
}
