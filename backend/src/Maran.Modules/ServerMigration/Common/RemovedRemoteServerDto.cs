namespace Maran.Modules.ServerMigration.Common;

/// <summary>
/// What the administrator is told when a remote server is removed: the panel's public key that is still installed on
/// that server, because the panel cannot take it out and the administrator must.
/// </summary>
/// <param name="PublicKey">The public key to remove from the server's <c>authorized_keys</c>.</param>
/// <param name="HostKeyFingerprint">The fingerprint of the host key that was pinned, or <c>null</c> if none was.</param>
public sealed record RemovedRemoteServerDto(string PublicKey, string? HostKeyFingerprint);
