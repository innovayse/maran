namespace Maran.Modules.Ssl.Domain;

/// <summary>
/// The panel's registration with one ACME certificate authority: the key it signs requests with, and
/// the account URL the authority gave back.
/// </summary>
/// <remarks>
/// Server-wide and not per-customer, which is why this entity carries no <c>AccountId</c> and no
/// tenant filter: the panel is the authority's customer, and every certificate on this machine is
/// ordered under the one registration. Giving each panel customer their own ACME account would
/// multiply the authority's per-account rate limits by nothing useful and leak the number of
/// customers on the server.
///
/// The key is kept because losing it costs real things: it is the only way to re-use the
/// authorizations this server has already earned, and creating a fresh account on every order runs
/// straight into the authority's new-account rate limit. It is stored ENCRYPTED — an ACME account key
/// is authority to issue certificates for every domain this account has validated, so plaintext in a
/// column is plaintext in every backup (rules/security.md item 8).
///
/// One row per directory URL: staging and production are different authorities with different
/// accounts, and a developer who switches between them must not send a staging account's key to the
/// production endpoint, which would be rejected in a way nothing explains.
/// </remarks>
public sealed class AcmeAccount
{
    /// <summary>The registration's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The authority's directory URL this registration belongs to.</summary>
    public string DirectoryUrl { get; private set; }

    /// <summary>The account URL the authority returned, sent as <c>kid</c> on every later request.</summary>
    public string AccountUrl { get; private set; }

    /// <summary>The PEM-encoded P-256 account key. Encrypted at rest by an EF value converter.</summary>
    public string PrivateKeyPem { get; private set; }

    /// <summary>When the panel registered with this authority.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a completed registration.</summary>
    /// <param name="id">The registration's identity.</param>
    /// <param name="directoryUrl">The authority's directory URL.</param>
    /// <param name="accountUrl">The account URL the authority returned.</param>
    /// <param name="privateKeyPem">The PEM-encoded account key.</param>
    /// <param name="createdAt">When the registration happened, taken from <see cref="IClock"/>.</param>
    public AcmeAccount(Guid id, string directoryUrl, string accountUrl, string privateKeyPem, DateTimeOffset createdAt)
    {
        Id = id;
        DirectoryUrl = directoryUrl;
        AccountUrl = accountUrl;
        PrivateKeyPem = privateKeyPem;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AcmeAccount()
    {
        DirectoryUrl = string.Empty;
        AccountUrl = string.Empty;
        PrivateKeyPem = string.Empty;
    }

    /// <summary>Describes the registration without revealing its key.</summary>
    /// <returns>A sentence naming the authority and nothing else.</returns>
    /// <remarks>
    /// Overridden for the same reason <c>IssuedCertificate</c> overrides it: the default
    /// <see cref="object.ToString"/> is harmless, but a future change to a record — or a logger that
    /// serializes an entity graph — would print every property, and this type has a property that
    /// must never be printed. Stating the safe rendering here means there is one to point at.
    /// </remarks>
    public override string ToString()
    {
        return $"AcmeAccount({DirectoryUrl})";
    }
}
