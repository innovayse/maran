namespace Maran.Modules.Sftp.Common;

/// <summary>Outward view of one public key registered for an SFTP login.</summary>
/// <remarks>
/// It carries no key body: the fingerprint is what a human compares against their own key, and the
/// body is public material the panel has no reason to send back (rules/security.md item 8).
/// </remarks>
/// <param name="Id">The key's identity, and the only identifier a request may name for it.</param>
/// <param name="Label">The customer's name for the key.</param>
/// <param name="Algorithm">The key's algorithm name, such as <c>ssh-ed25519</c>.</param>
/// <param name="Fingerprint"><c>SHA256:</c> plus the unpadded base64 of the SHA-256 of the key blob.</param>
/// <param name="CreatedAt">The instant the key was registered.</param>
public sealed record SftpUserKeyDto(
    Guid Id,
    string Label,
    string Algorithm,
    string Fingerprint,
    DateTimeOffset CreatedAt);
