namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>What a published mail DNS record is for, so a screen can name it in the reader's language.</summary>
public enum MailDnsPurpose
{
    /// <summary>The MX record that says where the domain's mail is delivered.</summary>
    Delivery,

    /// <summary>The SPF record that says which hosts may send for the domain.</summary>
    Spf,

    /// <summary>A DKIM public key that receivers verify signatures against.</summary>
    DkimKey,

    /// <summary>The DMARC record that says what receivers do with mail that fails SPF and DKIM.</summary>
    Dmarc,
}
