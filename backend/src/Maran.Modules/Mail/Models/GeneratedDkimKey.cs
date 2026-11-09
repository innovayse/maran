using Maran.SharedKernel.Security;

namespace Maran.Modules.Mail.Models;

/// <summary>A freshly generated DKIM key pair, before anything has been stored or sent.</summary>
/// <remarks>
/// The private half is held in a <see cref="SensitiveString"/> so a log line or a failed assertion cannot
/// print it by accident; it is encrypted for storage and unwrapped once, onto the wire to the agent.
/// </remarks>
/// <param name="PrivateKeyPem">The private key, PEM encoded (PKCS#8).</param>
/// <param name="PublicKeyBase64">The public key: base64 of its DER <c>SubjectPublicKeyInfo</c>, the value of the record's <c>p=</c> tag.</param>
public sealed record GeneratedDkimKey(SensitiveString PrivateKeyPem, string PublicKeyBase64);
