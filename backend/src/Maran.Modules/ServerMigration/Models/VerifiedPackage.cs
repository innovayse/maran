namespace Maran.Modules.ServerMigration.Models;

/// <summary>
/// A package whose head has held: the key is the one the operator confirmed, both signatures verify, and the
/// panel's header is bound to the agent's manifest. What follows in the stream is the artifact and nothing else
/// (issue #127).
/// </summary>
/// <remarks>
/// Verified says exactly this and no more: a valid signature proves only that the holder of the key signed, and
/// whether that holder is the server the operator believes it is rests on the fingerprint comparison the
/// operator made. The agent verifies the manifest again before it accepts a byte of the artifact.
/// </remarks>
/// <param name="Header">The panel's signed header.</param>
/// <param name="Manifest">The figures of the agent's manifest.</param>
/// <param name="ManifestBytes">The exact bytes the agent signed.</param>
/// <param name="ManifestSignature">The agent's signature over <paramref name="ManifestBytes"/>.</param>
/// <param name="PublicKey">The signer's uncompressed SEC1 point.</param>
/// <param name="SectionsJson">The header's sections as the JSON array the import row stores.</param>
public sealed record VerifiedPackage(
    MigrationPackageHeader Header,
    AgentTransferManifest Manifest,
    byte[] ManifestBytes,
    byte[] ManifestSignature,
    byte[] PublicKey,
    string SectionsJson);
