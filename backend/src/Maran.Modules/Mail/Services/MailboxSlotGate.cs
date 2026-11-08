using System.Buffers.Binary;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Persistence;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// Claims an account's mailbox allowance atomically, using a PostgreSQL advisory lock keyed by the account
/// so that the count, the quota sum and the write behind them cannot be interleaved by another request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a lock.</b> A unique index cannot express "at most N rows" or "at most M in total"; an insert
/// whose <c>WHERE</c> carries the count is not atomic under <c>READ COMMITTED</c>, where each statement
/// reads a snapshot and cannot see a concurrent transaction's uncommitted row; a serializable transaction
/// reports the conflict only at commit, after the handler has already made a mailbox on the host; and a
/// counter column with a check constraint would hold a copy of a figure that lives in the Accounts
/// module's plan, a second source of truth kept in step by nothing.
/// </para>
/// <para>
/// <b>What it cannot see, said plainly.</b> It serialises only callers that TAKE it, so it is a
/// convention rather than a constraint: a future write path that inserts a <see cref="Mailbox"/> without
/// coming through here is not held back by it. It is held for the length of one transaction, taken after
/// the host work is done, so no request waits on another's conversation with the root daemon.
/// </para>
/// </remarks>
public sealed class MailboxSlotGate : IMailboxSlotGate
{
    /// <summary>
    /// The advisory-lock namespace this module locks in (<c>MAIL</c> in ASCII), so its keys cannot collide
    /// with another module's and an operator reading <c>pg_locks</c> can tell whose lock it is.
    /// </summary>
    private const int AdvisoryLockNamespace = 0x4D41494C;

    /// <summary>The Mail module's context, and the connection the lock is taken on.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Creates the gate.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    public MailboxSlotGate(MailDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<MailboxSlotOutcome> TryTakeAsync(
        Mailbox mailbox,
        int allowance,
        int poolMib,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(mailbox.AccountId, cancellationToken);

        // Counted AFTER the lock and in their own statements, which is what makes this the deciding read:
        // READ COMMITTED gives each statement a fresh snapshot, so a request that waited here sees the
        // winner's committed row rather than the state it queued behind.
        var held = await _dbContext.Mailboxes
            .Where(existing => existing.AccountId == mailbox.AccountId)
            .Select(existing => existing.QuotaMib)
            .ToListAsync(cancellationToken);

        if (held.Count >= allowance)
        {
            await transaction.RollbackAsync(cancellationToken);

            return MailboxSlotOutcome.MailboxLimitReached;
        }

        if (held.Sum(quota =>
        {
            return (long)quota;
        }) + mailbox.QuotaMib > poolMib)
        {
            await transaction.RollbackAsync(cancellationToken);

            return MailboxSlotOutcome.QuotaPoolExhausted;
        }

        _dbContext.Mailboxes.Add(mailbox);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MailboxSlotOutcome.Taken;
    }

    /// <inheritdoc />
    public async Task<MailboxSlotOutcome> TryResizeAsync(
        Mailbox mailbox,
        int quotaMib,
        int sendLimitPerHour,
        int poolMib,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(mailbox.AccountId, cancellationToken);

        var others = await _dbContext.Mailboxes
            .Where(existing => existing.AccountId == mailbox.AccountId && existing.Id != mailbox.Id)
            .Select(existing => existing.QuotaMib)
            .ToListAsync(cancellationToken);

        if (others.Sum(quota =>
        {
            return (long)quota;
        }) + quotaMib > poolMib)
        {
            await transaction.RollbackAsync(cancellationToken);

            return MailboxSlotOutcome.QuotaPoolExhausted;
        }

        mailbox.ChangeLimits(quotaMib, sendLimitPerHour);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MailboxSlotOutcome.Taken;
    }

    /// <summary>Takes the account's transaction-scoped advisory lock.</summary>
    /// <param name="accountId">The account whose mailbox changes are being serialised.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    private async Task LockAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // raw-sql: pg_advisory_xact_lock has no LINQ translation and no EF Core equivalent — a lock scoped
        // to a transaction is a PostgreSQL facility. Parameterized through ExecuteSqlInterpolatedAsync
        // (rules/csharp.md "Data & tenancy"): both keys are integers this method computed.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock({AdvisoryLockNamespace}, {LockKeyFor(accountId)})",
            cancellationToken);
    }

    /// <summary>Derives this account's advisory-lock key from its identity.</summary>
    /// <param name="accountId">The account whose changes are being serialised.</param>
    /// <returns>The 32-bit second key of the advisory lock.</returns>
    /// <remarks>The first four bytes of the identifier. Deliberately NOT <c>GetHashCode</c>, which is unstable between runtimes.</remarks>
    private static int LockKeyFor(Guid accountId)
    {
        Span<byte> bytes = stackalloc byte[16];
        accountId.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
}
