using Maran.SharedKernel.Security;

namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The service's single-use permission to download one package. The grant id and the address both
/// identify the grant, so both are <see cref="SensitiveString"/>: neither prints, whatever a log line or
/// an exception message interpolates.
/// </summary>
/// <param name="GrantId">The single-use grant id.</param>
/// <param name="Url">The address to download from; it must be on the configured host.</param>
/// <param name="ExpiresAt">When the grant stops being accepted.</param>
/// <param name="Sha256">The package's SHA-256 the service declares, hexadecimal.</param>
/// <param name="SizeBytes">The package's size in bytes the service declares.</param>
/// <param name="Encrypted">Whether the package is encrypted for this panel (the loader decrypts it).</param>
public sealed record DownloadGrant(
    SensitiveString GrantId,
    SensitiveString Url,
    DateTimeOffset ExpiresAt,
    string Sha256,
    long SizeBytes,
    bool Encrypted = false);
