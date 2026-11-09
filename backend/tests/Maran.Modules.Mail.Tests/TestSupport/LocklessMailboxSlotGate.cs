using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// An <see cref="IMailboxSlotGate"/> with the same counting as the real one and none of its PostgreSQL:
/// no advisory lock and no explicit transaction, which the in-memory provider the handler tests run on has
/// neither of.
/// </summary>
/// <remarks>
/// A test can make the next claim lose a race the way a concurrent creation would, by setting
/// <see cref="RefuseNextClaim"/>. The real gate's locking is exercised by the PostgreSQL integration suite.
/// </remarks>
public sealed class LocklessMailboxSlotGate : IMailboxSlotGate
{
    /// <summary>The Mail module's context.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Creates the gate over a context.</summary>
    /// <param name="dbContext">The Mail module's context.</param>
    public LocklessMailboxSlotGate(MailDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>When true the next claim answers that the last slot was taken concurrently, then resets.</summary>
    public bool RefuseNextClaim { get; set; }

    /// <inheritdoc />
    public async Task<MailboxSlotOutcome> TryTakeAsync(
        Mailbox mailbox,
        int allowance,
        int poolMib,
        CancellationToken cancellationToken)
    {
        if (RefuseNextClaim)
        {
            RefuseNextClaim = false;

            return MailboxSlotOutcome.MailboxLimitReached;
        }

        var held = await _dbContext.Mailboxes
            .Where(existing => existing.AccountId == mailbox.AccountId)
            .Select(existing => existing.QuotaMib)
            .ToListAsync(cancellationToken);
        if (held.Count >= allowance)
        {
            return MailboxSlotOutcome.MailboxLimitReached;
        }

        if (held.Sum(quota =>
        {
            return (long)quota;
        }) + mailbox.QuotaMib > poolMib)
        {
            return MailboxSlotOutcome.QuotaPoolExhausted;
        }

        _dbContext.Mailboxes.Add(mailbox);
        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var others = await _dbContext.Mailboxes
            .Where(existing => existing.AccountId == mailbox.AccountId && existing.Id != mailbox.Id)
            .Select(existing => existing.QuotaMib)
            .ToListAsync(cancellationToken);
        if (others.Sum(quota =>
        {
            return (long)quota;
        }) + quotaMib > poolMib)
        {
            return MailboxSlotOutcome.QuotaPoolExhausted;
        }

        mailbox.ChangeLimits(quotaMib, sendLimitPerHour);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MailboxSlotOutcome.Taken;
    }
}
