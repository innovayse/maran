namespace Maran.Agent.Client.Services.BackupService;

/// <summary>An artifact the agent could describe, and what its sidecar said about it.</summary>
/// <remarks>
/// <see cref="Manifest"/> is NOT nullable, and that is the whole point of this type existing rather
/// than three loose members on <see cref="AgentBackupSummary"/>. On the wire the manifest is a
/// message field, which proto3 permits to be absent even inside the <c>readable</c> arm of the
/// oneof: the arm is exactly-one, the manifest inside it is not guaranteed. So the guarantee is
/// total in the agent's own Rust types and only partial on the wire, and this is where the panel
/// restores it — <c>AgentBackupClient.ListAsync</c> refuses a <c>readable</c> arm whose manifest is
/// absent instead of constructing one of these with a null in it.
/// </remarks>
/// <param name="Manifest">The manifest, exactly as it was written into the archive.</param>
/// <param name="ArtifactBytes">Size of the published archive, in bytes.</param>
/// <param name="ArtifactSha256">SHA-256 of the published archive, hex, lowercase.</param>
public sealed record AgentReadableBackup(
    AgentBackupManifest Manifest,
    ulong ArtifactBytes,
    string ArtifactSha256);
