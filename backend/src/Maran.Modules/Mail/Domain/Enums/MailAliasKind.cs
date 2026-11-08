namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>The three things an address that is not a mailbox can be.</summary>
public enum MailAliasKind
{
    /// <summary>An address whose every target is a mailbox on a domain the account hosts.</summary>
    Alias,

    /// <summary>An address with at least one target on a domain the account does not host.</summary>
    Forwarder,

    /// <summary>The address that receives mail for any local part of its domain that nothing else claims.</summary>
    CatchAll,
}
