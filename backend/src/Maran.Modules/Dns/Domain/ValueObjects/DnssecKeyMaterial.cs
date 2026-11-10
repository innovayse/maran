using Maran.SharedKernel.Security;

namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>
/// A freshly generated signing key, before it is a row. The private scalar is a <see cref="SensitiveString"/>,
/// so the generated <c>ToString</c> of this record, a log argument or an exception message prints a mask.
/// </summary>
/// <param name="PublicKey">The public key as standard base64 of the 64 bytes <c>x || y</c>.</param>
/// <param name="PrivateKey">The private scalar as standard base64 of its 32 bytes. A secret.</param>
/// <param name="Flags">The DNSKEY flags.</param>
/// <param name="Algorithm">The DNSSEC algorithm number.</param>
/// <param name="KeyTag">The RFC 4034 appendix B key tag of the DNSKEY.</param>
public sealed record DnssecKeyMaterial(
    string PublicKey,
    SensitiveString PrivateKey,
    int Flags,
    int Algorithm,
    int KeyTag);
