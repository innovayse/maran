using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Interfaces;

/// <summary>
/// The last word on whether an account may hold one more mailbox, or one mailbox larger, taken at the
/// moment the row is written rather than before the host is touched.
/// </summary>
/// <remarks>
/// <para>
/// The plan's allowance is a statement about a ROW COUNT and about a SUM, and no key can express either:
/// a unique index refuses a repeated value, not "at most N rows" and not "at most M mebibytes in total".
/// So the count, the sum and the write are made one step by a per-account advisory lock inside a
/// transaction, which is the only honest implementation and speaks PostgreSQL — which is why it is a seam:
/// most handler tests run on a provider that has neither.
/// </para>
/// <para>
/// A caller reaches this only AFTER the agent has made the mailbox, so a refusal means a mailbox exists
/// on the host that no row will own: the caller compensates, exactly as a failed write is compensated.
/// The handler still checks the allowance first so the ordinary over-limit request never touches the host.
/// A database failure is thrown and never answered as a refusal, because the caller's answer to a
/// duplicate key and to a lost connection differ and only one of them compensates.
/// </para>
/// </remarks>
public interface IMailboxSlotGate
{
    /// <summary>Writes the mailbox if, and only if, the account is still inside its mailbox count and its quota pool.</summary>
    /// <param name="mailbox">The row to write, for a mailbox the agent has already created.</param>
    /// <param name="allowance">How many mailboxes the account's plan allows in total.</param>
    /// <param name="poolMib">The most mebibytes the account's mailbox quotas may add up to.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The outcome; nothing was written unless it is <see cref="MailboxSlotOutcome.Taken"/>.</returns>
    Task<MailboxSlotOutcome> TryTakeAsync(
        Mailbox mailbox,
        int allowance,
        int poolMib,
        CancellationToken cancellationToken);

    /// <summary>Changes a tracked mailbox's quota and send limit if the account's quota pool still holds the new quota.</summary>
    /// <param name="mailbox">The tracked mailbox to change.</param>
    /// <param name="quotaMib">The new quota, in MiB.</param>
    /// <param name="sendLimitPerHour">The new hourly send limit.</param>
    /// <param name="poolMib">The most mebibytes the account's mailbox quotas may add up to.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns><see cref="MailboxSlotOutcome.Taken"/> when the change was committed, or <see cref="MailboxSlotOutcome.QuotaPoolExhausted"/>.</returns>
    Task<MailboxSlotOutcome> TryResizeAsync(
        Mailbox mailbox,
        int quotaMib,
        int sendLimitPerHour,
        int poolMib,
        CancellationToken cancellationToken);
}
