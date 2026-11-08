namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>The password hashing scheme the host's IMAP and POP3 server was observed to verify.</summary>
public enum MailPasswordScheme
{
    /// <summary>Argon2id, the scheme the panel hashes mailbox passwords in.</summary>
    Argon2id,

    /// <summary>Blowfish crypt, the scheme of a host whose Dovecot build lacks Argon2id.</summary>
    BlowfishCrypt,
}
