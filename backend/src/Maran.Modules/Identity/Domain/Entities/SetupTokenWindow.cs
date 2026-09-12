namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// When this panel first observed the installer's one-time setup token, which is what the token's
/// expiry is measured from. At most one row, ever.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the panel needs a row at all.</b> The token is permission to become the administrator of
/// the whole server, and until the first administrator exists it is the whole server. Nothing about
/// it carried a time: the installer writes the value into <c>panel.env</c> and no issue time beside
/// it, so an install nobody finished left a LIVE token for as long as the file existed. An expiry
/// needs an instant to count from, and the only one the panel can observe for itself is the first
/// moment it ran with that token configured.
/// </para>
/// <para>
/// <b>It stores a FINGERPRINT, never the token.</b> A token is a secret and a secret does not go
/// into a table that a database dump copies (rules/security.md item 8). SHA-256 of the value is
/// enough for the one question asked of it — is the configured token still the one whose window is
/// open — and is not the token: an operator who rotates the value gets a new fingerprint, which is
/// read as a new token and opens a new window, and that is the mechanism by which an expired install
/// is recovered rather than a special case beside it.
/// </para>
/// <para>
/// <b>A singleton by construction.</b> The key is <see cref="SingletonId"/> and nothing generates
/// another, so the table cannot hold two windows for one panel — two would mean two answers to "when
/// did the clock start" and the later one would extend a token the earlier one had expired.
/// </para>
/// <para>
/// <b>What it deliberately is not: a record of the token being SPENT.</b> That question is already
/// answered by whether any user exists, which is state rather than a flag and therefore has no "mark
/// as used" step that can fail. This row narrows the window in which the unspent token works; it
/// does not become the authority on whether setup is done.
/// </para>
/// </remarks>
public sealed class SetupTokenWindow
{
    /// <summary>The one primary key this table ever holds.</summary>
    /// <remarks>
    /// A fixed value rather than a generated one, so "open the window if it is not open" is a single
    /// primary-key lookup and two concurrent starts cannot each create a row.
    /// </remarks>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000005354");

    /// <summary>The row's identity, always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the configured token; never the token itself.</summary>
    public string TokenFingerprint { get; private set; }

    /// <summary>The instant this panel first observed that token, taken from <c>IClock</c>.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>Opens a window for a token this panel has not observed before.</summary>
    /// <param name="tokenFingerprint">Lowercase hex SHA-256 of the configured token.</param>
    /// <param name="openedAt">The instant the token was first observed.</param>
    public SetupTokenWindow(string tokenFingerprint, DateTimeOffset openedAt)
    {
        Id = SingletonId;
        TokenFingerprint = tokenFingerprint;
        OpenedAt = openedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SetupTokenWindow()
    {
        TokenFingerprint = string.Empty;
    }

    /// <summary>Restarts the window because the configured token is a different one.</summary>
    /// <param name="tokenFingerprint">Lowercase hex SHA-256 of the token now configured.</param>
    /// <param name="openedAt">The instant this panel first observed it.</param>
    /// <remarks>
    /// The only mutation this entity has, and it is what makes an expired install recoverable: an
    /// operator replaces the token in the panel's environment and restarts, the fingerprint no longer
    /// matches, and the clock starts again. Keeping the old instant instead would leave a panel whose
    /// new token was born expired, with nothing an operator could do about it.
    /// </remarks>
    public void Reopen(string tokenFingerprint, DateTimeOffset openedAt)
    {
        TokenFingerprint = tokenFingerprint;
        OpenedAt = openedAt;
    }
}
