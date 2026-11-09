namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>
/// Whether a mail domain is receiving and sending: a reading of its account, not a stored column,
/// because the only thing that stops a domain is its account being suspended.
/// </summary>
public enum MailDomainStatus
{
    /// <summary>The domain receives and sends mail.</summary>
    Active,

    /// <summary>The domain's account is suspended, so its mail is refused.</summary>
    Suspended,
}
