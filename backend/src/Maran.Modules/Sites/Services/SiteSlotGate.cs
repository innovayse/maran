using System.Buffers.Binary;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Interfaces;
using Maran.Modules.Sites.Persistence;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Takes the account's last free site slot atomically, using a PostgreSQL advisory lock keyed by the
/// account so that the count and the insert behind it cannot be interleaved by another request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a lock at all, when the other race in this handler is closed by a key.</b> The domain race
/// and the limit race look alike and are not. Each of the obvious alternatives was considered and each
/// is blind to something:
/// </para>
/// <para>
/// A <b>unique index</b> cannot express "at most N rows for this account": an index refuses a repeated
/// VALUE, and the allowance is a cardinality. It is exactly what closes the two-sites-one-domain case,
/// through the hostname key, and it cannot be made to close this one.
/// </para>
/// <para>
/// An <b>insert whose <c>WHERE</c> carries the count</b> looks atomic and is not. Under
/// <c>READ COMMITTED</c> — the panel's default isolation — each statement reads a snapshot taken when
/// that statement began, and an uncommitted row from a concurrent transaction is invisible in it. So
/// two <c>insert … select … where (select count(*) …) &lt; N</c> statements running at once both see
/// N-1 and both insert. It cannot see the other transaction's row, which is the entire problem.
/// </para>
/// <para>
/// A <b>serializable transaction</b> would see it — the read-write conflict is exactly what
/// <c>SERIALIZABLE</c> detects — but it reports it as <c>40001</c> at COMMIT time, so correctness there
/// costs a retry loop around a handler that has already provisioned a vhost and a php-fpm pool on the
/// host, and the retry would have to decide whether the host work may be repeated. It also cannot be
/// scoped to one account: the isolation level is a property of the transaction, so every unrelated
/// write in it pays the serialization failure too.
/// </para>
/// <para>
/// A <b>counter column with a check constraint</b> cannot see the allowance. The number lives in the
/// Accounts module's plan and this module may not read that schema (rules/architecture.md), so a check
/// constraint here would have to be written against a copy of a figure that changes when a customer's
/// plan changes — a second source of truth for the limit, kept in step by nothing.
/// </para>
/// <para>
/// <b>The agent contributes no serialization here at all, unlike the login modules.</b>
/// <c>ops::sites::create_site</c> takes no per-account lock — it is not among the operations that call
/// <c>take_account_lock</c> — so nothing on the agent side narrows this window even slightly. Where the
/// Ftp and Sftp modules have a wait-free account lock that turns away an exactly simultaneous second
/// caller, this module has nothing, and the gate below is the whole of the protection.
/// </para>
/// <para>
/// <b>What the advisory lock cannot see, said plainly.</b> It serialises only callers that TAKE it, so
/// it is a convention rather than a constraint: a future write path that inserts a
/// <see cref="Site"/> without coming through here is not held back by it, and the database will not
/// object. That is the price of expressing a cardinality at all, and it is why the pre-agent check and
/// this gate are both documented as the same protection seen twice rather than as two. It also does not
/// survive being held across the agent round trip, which is why it is not: the lock is taken after the
/// host work is done and released by the commit microseconds later, so no request ever waits on
/// another request's conversation with the root daemon.
/// </para>
/// <para>
/// <b>Its blind spot within one panel is a key collision.</b> The lock key is derived from the account
/// id, and two different accounts whose ids collide in it would serialise against each other — which
/// costs a few microseconds and changes no answer. The reverse, two requests for ONE account taking
/// different keys, cannot happen: the derivation is a pure function of the id.
/// </para>
/// </remarks>
public sealed class SiteSlotGate : ISiteSlotGate
{
    /// <summary>
    /// The advisory-lock namespace this module locks in, so its keys cannot collide with another
    /// module's.
    /// </summary>
    /// <remarks>
    /// PostgreSQL's two-argument advisory locks take a pair of 32-bit keys and treat the pair as the
    /// lock's identity, so the first key is by convention a namespace. This one spells <c>SITE</c> in
    /// ASCII, which is readable in <c>pg_locks</c> — an operator looking at a blocked session can tell
    /// whose lock it is without a table of magic numbers.
    /// </remarks>
    private const int AdvisoryLockNamespace = 0x53495445;

    /// <summary>The Sites module's database context, and the connection the lock is taken on.</summary>
    /// <remarks>
    /// The lock is scoped to a TRANSACTION on this context's connection, so it is released by the
    /// commit or the rollback and cannot outlive the request even if the process is killed — a
    /// session-scoped lock would need a release that a crash could skip.
    /// </remarks>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the gate.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    public SiteSlotGate(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<bool> TryTakeAsync(Site site, int allowance, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // raw-sql: pg_advisory_xact_lock has no LINQ translation and no EF Core equivalent — a lock
        // scoped to a transaction is a PostgreSQL facility. Parameterized through
        // ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy"): both keys are integers this
        // method computed, and neither is caller text, but they are parameters rather than
        // interpolated SQL because nothing in this file should have to be re-read to know that.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock({AdvisoryLockNamespace}, {LockKeyFor(site.AccountId)})",
            cancellationToken);

        // Counted AFTER the lock and in its own statement, which is what makes this the deciding read:
        // READ COMMITTED gives each statement a fresh snapshot, so a request that waited here sees the
        // winner's committed row rather than the state it queued behind.
        var taken = await _dbContext.Sites
            .CountAsync(existing => existing.AccountId == site.AccountId, cancellationToken);
        if (taken >= allowance)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        _dbContext.Sites.Add(site);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>Derives this account's advisory-lock key from its identity.</summary>
    /// <param name="accountId">The account whose creations are being serialised.</param>
    /// <returns>The 32-bit second key of the advisory lock.</returns>
    /// <remarks>
    /// The first four bytes of the identifier, read as an integer. Deliberately NOT
    /// <c>GetHashCode</c>: that is documented as unstable between runtimes, and a key that changed
    /// between two processes reading the same database would silently stop serialising them. These
    /// bytes are a property of the value.
    /// </remarks>
    private static int LockKeyFor(Guid accountId)
    {
        Span<byte> bytes = stackalloc byte[16];
        accountId.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
}
