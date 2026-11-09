using Maran.Modules.Mail.Domain.Entities;

namespace Maran.Modules.Mail.Extensions;

/// <summary>The one way an operation loads a mailbox it is about to act on.</summary>
/// <remarks>
/// Five operations (change limits, change password, delete, set and clear the autoresponder) start the
/// same way, and the part worth having once is what they must NOT do differently: they read through the
/// tenant-filtered context, so another account's mailbox is simply absent and every one of them answers
/// <c>MailboxNotFound</c> for it, indistinguishable from a mailbox that does not exist.
/// </remarks>
public static class MailboxLookupExtensions
{
    /// <summary>Loads a tracked mailbox together with the domain it lives in.</summary>
    /// <param name="mailboxes">The mailbox set of the tenant-filtered context.</param>
    /// <param name="mailboxId">Which mailbox.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The mailbox with its domain loaded, or <c>null</c> when the caller may not see one by that id.
    /// </returns>
    public static async Task<Mailbox?> FindWithDomainAsync(
        this DbSet<Mailbox> mailboxes,
        Guid mailboxId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);

        return await mailboxes
            .Include(mailbox => mailbox.MailDomain)
            .SingleOrDefaultAsync(mailbox => mailbox.Id == mailboxId, cancellationToken);
    }
}
