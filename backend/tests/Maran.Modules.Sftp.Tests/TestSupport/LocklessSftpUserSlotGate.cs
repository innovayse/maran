using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Interfaces;
using Maran.Modules.Sftp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>
/// The slot gate as this project can run it: the same count-and-insert decision, without the
/// PostgreSQL advisory lock and transaction the real one is built on.
/// </summary>
/// <remarks>
/// <para>
/// It is a stand-in rather than the real gate because the real gate's whole content is a PostgreSQL
/// facility — <c>pg_advisory_xact_lock</c> inside an explicit transaction — and the provider these
/// tests run on has neither. Using the real one here would not measure the lock; it would throw on
/// the first statement.
/// </para>
/// <para>
/// <b>So what this project can and cannot say about the limit.</b> It can say that the decision is
/// taken from the account's own rows against the plan's figure, and that the handler answers each of
/// the gate's two outcomes correctly — which is what the tests over it assert.
/// <b>UNOBSERVED HERE: that two simultaneous creations cannot both pass.</b> That is a property of
/// the lock, it is invisible to any test on this provider, and it is measured against real PostgreSQL
/// by <c>Maran.Host.IntegrationTests.SftpUserLimitRaceTests</c>.
/// </para>
/// </remarks>
public sealed class LocklessSftpUserSlotGate : ISftpUserSlotGate
{
    /// <summary>The module's database, counted and written through.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the stand-in.</summary>
    /// <param name="dbContext">The module's database.</param>
    public LocklessSftpUserSlotGate(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Set by a test to make the next claim report the account's last slot as taken.</summary>
    /// <remarks>
    /// The handler's late-refusal branch is reachable no other way on this provider: it needs the
    /// gate to refuse a request that the pre-agent check had already accepted, which in production is
    /// a concurrent commit and here is this flag.
    /// </remarks>
    public bool RefuseNextClaim { get; set; }

    /// <inheritdoc />
    public async Task<bool> TryTakeAsync(SftpUser sftpUser, int allowance, CancellationToken cancellationToken)
    {
        if (RefuseNextClaim)
        {
            RefuseNextClaim = false;

            return false;
        }

        var taken = await _dbContext.SftpUsers
            .CountAsync(existing => existing.AccountId == sftpUser.AccountId, cancellationToken);
        if (taken >= allowance)
        {
            return false;
        }

        _dbContext.SftpUsers.Add(sftpUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
