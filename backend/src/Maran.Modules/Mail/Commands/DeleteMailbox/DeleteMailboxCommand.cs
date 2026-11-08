namespace Maran.Modules.Mail.Commands.DeleteMailbox;

/// <summary>
/// Removes a mailbox and the mail it holds. The data goes with it: a deleted mailbox cannot be brought
/// back, which is what the caller asked for and why the screen asks twice.
/// </summary>
/// <param name="MailboxId">
/// Which mailbox to remove. A row identifier and never an address: the row says who owns the mailbox, and
/// another account's identifier answers "not found".
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteMailboxCommand(Guid MailboxId, string IpAddress, string UserAgent);
