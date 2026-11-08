using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IDnsZoneSlotGate"/> for the in-memory provider: it counts and inserts with no lock, which is
/// all a single-threaded test needs. The race the real gate closes is a PostgreSQL property.
/// </summary>
public sealed class LocklessDnsZoneSlotGate : IDnsZoneSlotGate
{
    /// <summary>The context counted and inserted through.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>Set to make the gate answer as if another creation had taken the last slot.</summary>
    public bool RefuseAlways { get; set; }

    /// <summary>Creates the stand-in over the test's context.</summary>
    /// <param name="dbContext">The test's context.</param>
    public LocklessDnsZoneSlotGate(DnsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<bool> TryTakeAsync(Zone zone, int allowance, CancellationToken cancellationToken)
    {
        var taken = await _dbContext.Zones.CountAsync(row => row.AccountId == zone.AccountId, cancellationToken);
        if (RefuseAlways || taken >= allowance)
        {
            return false;
        }

        _dbContext.Zones.Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
