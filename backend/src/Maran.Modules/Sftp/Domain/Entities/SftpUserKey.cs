using Maran.Modules.Sftp.Domain.ValueObjects;

namespace Maran.Modules.Sftp.Domain.Entities;

/// <summary>
/// One public key a customer registered for one SFTP login: the label they gave it, the one-line key
/// the agent writes into the login's key file, and the fingerprint the panel computed from it.
/// </summary>
/// <remarks>
/// <para>
/// The panel is the source of truth for a login's key set and the agent converges to whatever FULL
/// set it is sent, so these rows are read back as a whole whenever the set changes. The body is
/// public material but it ends up in a file a root process writes, so it is stored once, in the
/// validated <c>&lt;algorithm&gt; &lt;base64&gt;</c> form with options and comment dropped
/// (<see cref="SftpPublicKey"/>), and it is never returned by a list, logged or audited; the
/// fingerprint is what identifies the key everywhere a human or a journal needs to name it.
/// </para>
/// <para>
/// Rows are created by <see cref="SftpUser.AddKey"/> and by nothing else, so a key cannot exist
/// without its login, without a valid label, or past the login's limit. They are removed with the
/// login by the relationship's cascade. A key carries no account id of its own: the tenant query
/// filter reaches the owning account through the login, so the two cannot disagree.
/// </para>
/// </remarks>
public sealed class SftpUserKey
{
    /// <summary>The row's identity, and the only identifier a customer's request may name for a key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The login this key lets in.</summary>
    public Guid SftpUserId { get; private set; }

    /// <summary>The login this key lets in, as the relationship EF Core cascades on.</summary>
    /// <remarks>
    /// Present so the tenant query filter can reach the owning account through it rather than
    /// storing a second copy of the account id that could disagree with the login's own.
    /// </remarks>
    public SftpUser SftpUser { get; private set; }

    /// <summary>The customer's name for the key: trimmed, 1 to 64 characters, no control character.</summary>
    public string Label { get; private set; }

    /// <summary>The key as <c>&lt;algorithm&gt; &lt;base64&gt;</c>, exactly what the agent is sent.</summary>
    public string PublicKey { get; private set; }

    /// <summary><c>SHA256:</c> plus unpadded base64 of the SHA-256 of the key blob; unique within the login.</summary>
    public string Fingerprint { get; private set; }

    /// <summary>The key's algorithm name, for display.</summary>
    public string Algorithm { get; private set; }

    /// <summary>The instant the key was registered.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a key of <paramref name="sftpUser"/>; made only by <see cref="SftpUser.AddKey"/>.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="sftpUser">The login the key lets in.</param>
    /// <param name="label">The already validated, trimmed label.</param>
    /// <param name="publicKey">The parsed key.</param>
    /// <param name="createdAt">The registration instant, taken from <see cref="IClock"/>.</param>
    internal SftpUserKey(Guid id, SftpUser sftpUser, string label, SftpPublicKey publicKey, DateTimeOffset createdAt)
    {
        Id = id;
        SftpUser = sftpUser;
        SftpUserId = sftpUser.Id;
        Label = label;
        PublicKey = publicKey.Line;
        Fingerprint = publicKey.Fingerprint;
        Algorithm = publicKey.Algorithm;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SftpUserKey()
    {
        SftpUser = null!;
        Label = string.Empty;
        PublicKey = string.Empty;
        Fingerprint = string.Empty;
        Algorithm = string.Empty;
    }
}
