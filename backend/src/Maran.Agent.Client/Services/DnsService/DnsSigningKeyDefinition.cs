using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.DnsService;

/// <summary>
/// The one ECDSA P-256 key that signs a zone, as the agent is handed it. The private half is a
/// <see cref="SensitiveString"/>, so a record's generated <c>ToString</c>, a log argument or an exception
/// message prints a mask and never the scalar.
/// </summary>
/// <param name="PublicKey">The public key as standard base64 of the 64 bytes <c>x || y</c>.</param>
/// <param name="PrivateKey">The private scalar as standard base64 of its 32 bytes. A secret.</param>
/// <param name="Flags">The DNSKEY flags: 257 for the combined signing key.</param>
/// <param name="KeyTag">The RFC 4034 appendix B key tag the panel computed; the agent recomputes and compares.</param>
public sealed record DnsSigningKeyDefinition(
    string PublicKey,
    SensitiveString PrivateKey,
    int Flags,
    int KeyTag);
