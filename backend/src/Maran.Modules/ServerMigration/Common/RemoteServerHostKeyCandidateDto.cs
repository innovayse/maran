namespace Maran.Modules.ServerMigration.Common;

/// <summary>One host key a remote server offered, for the administrator to compare against the server's own console.</summary>
/// <param name="Algorithm">The key's algorithm, such as <c>ssh-ed25519</c>.</param>
/// <param name="Fingerprint">The <c>SHA256:</c> fingerprint to confirm.</param>
public sealed record RemoteServerHostKeyCandidateDto(string Algorithm, string Fingerprint);
