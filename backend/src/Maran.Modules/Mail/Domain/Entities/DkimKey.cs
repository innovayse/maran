using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// One DKIM signing key of a mail domain: its selector, the private half (encrypted at rest) and the
/// public half the domain's DNS record carries.
/// </summary>
/// <remarks>
/// <para>
/// The private key is stored ciphertext only, produced by <c>IEncryptionService</c>, and no DTO,
/// export or log line carries it: it leaves the panel exactly once per configure call, to the agent,
/// inside a <c>SensitiveString</c>. There is deliberately no method that returns it decrypted; the
/// service that sends it to the host is the only reader.
/// </para>
/// <para>
/// The row has no <c>AccountId</c> of its own: it is scoped through its required domain, so a second
/// copy of the account that could disagree with the domain's does not exist.
/// </para>
/// </remarks>
public sealed class DkimKey
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The domain this key signs for.</summary>
    public Guid MailDomainId { get; private set; }

    /// <summary>The domain, loaded when the key is read through it.</summary>
    public MailDomain MailDomain { get; private set; } = null!;

    /// <summary>The DNS label the public key is published under: <c>selector._domainkey</c>.</summary>
    public string Selector { get; private set; }

    /// <summary>The private key as PEM, encrypted with the panel's encryption key.</summary>
    public string PrivateKeyEncrypted { get; private set; }

    /// <summary>The public key, base64 of its DER <c>SubjectPublicKeyInfo</c>, as the DNS record's <c>p=</c> tag carries it.</summary>
    public string PublicKey { get; private set; }

    /// <summary>Where the key is in its life.</summary>
    public DkimKeyState State { get; private set; }

    /// <summary>The instant the key was generated.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The instant the key stopped being the signing key, or <c>null</c> while it still is.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Records a freshly generated key as the active one.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="mailDomainId">The domain the key signs for.</param>
    /// <param name="selector">The selector, see <c>DkimSelectorPolicy</c>.</param>
    /// <param name="privateKeyEncrypted">The encrypted PEM.</param>
    /// <param name="publicKey">The base64 public key.</param>
    /// <param name="createdAt">The generation instant, taken from <see cref="IClock"/>.</param>
    public DkimKey(
        Guid id,
        Guid mailDomainId,
        string selector,
        string privateKeyEncrypted,
        string publicKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        MailDomainId = mailDomainId;
        Selector = selector;
        PrivateKeyEncrypted = privateKeyEncrypted;
        PublicKey = publicKey;
        State = DkimKeyState.Active;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DkimKey()
    {
        Selector = string.Empty;
        PrivateKeyEncrypted = string.Empty;
        PublicKey = string.Empty;
    }

    /// <summary>Marks the key as replaced while its record is still published.</summary>
    /// <param name="at">The instant of the rotation.</param>
    public void Retire(DateTimeOffset at)
    {
        if (State != DkimKeyState.Active)
        {
            return;
        }

        State = DkimKeyState.Retiring;
        RetiredAt = at;
    }

    /// <summary>Marks a replaced key as no longer needing its record.</summary>
    public void Expire()
    {
        if (State == DkimKeyState.Retiring)
        {
            State = DkimKeyState.Retired;
        }
    }
}
