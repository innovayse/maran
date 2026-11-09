namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>The DNS record types the mail records of a domain use.</summary>
public enum MailDnsRecordType
{
    /// <summary>A mail exchanger record: where mail for the domain is delivered.</summary>
    Mx,

    /// <summary>A text record: the SPF policy, the DKIM public key or the DMARC policy.</summary>
    Txt,
}
