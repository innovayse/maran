using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Models;

/// <summary>
/// The panel's half of a package: what the modules said about the account, signed by the sending server's key
/// and bound to the agent's manifest by its digest (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// The wire shape of the signed header, so every member is data the receiving panel treats as untrusted until
/// the signature and the confirmed fingerprint have held. It binds to the agent's manifest through
/// <paramref name="AgentManifestSha256"/> and not by repeating its figures, so there is one statement of the
/// artifact's size and digest and the two halves cannot disagree.
/// </para>
/// <para>
/// No secret is a member: sections are allow-lists of named fields (<see cref="MigrationSection"/>).
/// </para>
/// </remarks>
/// <param name="Schema">The version of this shape; the reader refuses one it does not know.</param>
/// <param name="SourceHostName">The name the sending server gives itself, for the operator's recognition and nothing else.</param>
/// <param name="KeyFingerprint">The fingerprint of the key that signed, repeated so a header cut from its key is refused.</param>
/// <param name="AccountName">The account's system user name.</param>
/// <param name="HomeBytes">The size of the home directory in the artifact.</param>
/// <param name="AgentManifestSha256">The lowercase hexadecimal SHA-256 of the agent's manifest bytes that follow in the package.</param>
/// <param name="CreatedAt">When the package was made.</param>
/// <param name="Sections">What each module contributed.</param>
public sealed record MigrationPackageHeader(
    int Schema,
    string SourceHostName,
    string KeyFingerprint,
    string AccountName,
    long HomeBytes,
    string AgentManifestSha256,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MigrationSection> Sections);
