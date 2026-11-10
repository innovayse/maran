using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Models;

/// <summary>This server's signing key as the services use it: the private half masked, the public half and its fingerprint plain.</summary>
/// <remarks>
/// A record's generated <c>ToString()</c> prints every member, so the private half is a <see cref="SensitiveString"/>:
/// formatting this value, logging it or letting an exception quote it prints a mask.
/// </remarks>
/// <param name="PrivateKey">The PKCS#8 DER private key, base64-encoded.</param>
/// <param name="PublicKey">The uncompressed SEC1 public point.</param>
/// <param name="Fingerprint">The lowercase hexadecimal SHA-256 of <paramref name="PublicKey"/>.</param>
public sealed record ServerSigningKey(SensitiveString PrivateKey, byte[] PublicKey, string Fingerprint);
