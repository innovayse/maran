namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for the mail the panel hosts for its customers: mail domains,
/// mailboxes, aliases and forwarders, and the server's mail daemons. Constants rather than an enum: a
/// marketplace module records actions this assembly was never compiled knowing about, and an enum
/// could not be extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record. Distinct from <see cref="OutgoingMailAuditActions"/> on purpose: that class names
/// the panel's OWN outgoing mail (SMTP settings, test messages, alerts), a different facility owned by
/// the Notifications module, and the two share a word and nothing else.
/// </remarks>
public static class HostedMailAuditActions
{
    /// <summary>The server's mail daemons were configured and started.</summary>
    public const string MailServerEnabled = "MailServerEnabled";

    /// <summary>The server's mail daemons were stopped and taken out of the boot sequence.</summary>
    public const string MailServerDisabled = "MailServerDisabled";

    /// <summary>The server's mail settings (hostname, size limit, spam thresholds) were changed.</summary>
    public const string MailServerSettingsUpdated = "MailServerSettingsUpdated";

    /// <summary>A queued message was removed from the mail queue.</summary>
    public const string MailQueueMessageDeleted = "MailQueueMessageDeleted";

    /// <summary>The whole mail queue was flushed for another delivery attempt.</summary>
    public const string MailQueueFlushed = "MailQueueFlushed";

    /// <summary>A mail domain was added to an account.</summary>
    public const string MailDomainCreated = "MailDomainCreated";

    /// <summary>A mail domain was removed from an account.</summary>
    public const string MailDomainDeleted = "MailDomainDeleted";

    /// <summary>A mail domain's DKIM signing key was replaced by a new one.</summary>
    public const string MailDkimKeyRotated = "MailDkimKeyRotated";

    /// <summary>A mailbox was created.</summary>
    public const string MailboxCreated = "MailboxCreated";

    /// <summary>A mailbox's quota, send limit or status was changed.</summary>
    public const string MailboxUpdated = "MailboxUpdated";

    /// <summary>A mailbox was given a new password.</summary>
    public const string MailboxPasswordChanged = "MailboxPasswordChanged";

    /// <summary>A mailbox was removed.</summary>
    public const string MailboxDeleted = "MailboxDeleted";

    /// <summary>An alias or forwarder was created.</summary>
    public const string MailAliasCreated = "MailAliasCreated";

    /// <summary>An alias or forwarder was changed.</summary>
    public const string MailAliasUpdated = "MailAliasUpdated";

    /// <summary>An alias or forwarder was removed.</summary>
    public const string MailAliasDeleted = "MailAliasDeleted";

    /// <summary>A mail domain's catch-all address was set or its targets changed.</summary>
    public const string MailCatchAllSet = "MailCatchAllSet";

    /// <summary>A mail domain's catch-all address was removed.</summary>
    public const string MailCatchAllCleared = "MailCatchAllCleared";

    /// <summary>A mailbox's autoresponder was set or its text changed.</summary>
    public const string MailAutoresponderSet = "MailAutoresponderSet";

    /// <summary>A mailbox's autoresponder was removed.</summary>
    public const string MailAutoresponderCleared = "MailAutoresponderCleared";
}
