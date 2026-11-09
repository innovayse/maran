namespace Maran.Modules.Mail.Queries.GetMailboxAutoresponder;

/// <summary>Reads the automatic reply one mailbox sends.</summary>
/// <param name="MailboxId">
/// The mailbox. A row identifier from the route; another account's identifier answers "not found".
/// </param>
public sealed record GetMailboxAutoresponderQuery(Guid MailboxId);
