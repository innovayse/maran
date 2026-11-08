namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>What happens to mail the spam filter scores as spam, as a mailbox owner chose.</summary>
public enum MailSpamAction
{
    /// <summary>The message is filed into the mailbox's junk folder.</summary>
    FileToJunk,

    /// <summary>The message is left in the inbox, marked by a header only.</summary>
    KeepInInbox,
}
