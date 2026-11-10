namespace Maran.Modules.Dns.Common;

/// <summary>The public DNSKEY record of a zone, for a registrar that asks for the key instead of the DS record.</summary>
/// <param name="Flags">The DNSKEY flags: 257 for the combined signing key.</param>
/// <param name="Protocol">The DNSKEY protocol field, always 3.</param>
/// <param name="Algorithm">The DNSSEC algorithm number: 13 for ECDSA P-256.</param>
/// <param name="PublicKey">The public key as standard base64. The private half is never part of any answer.</param>
public sealed record DnskeyRecordDto(int Flags, int Protocol, int Algorithm, string PublicKey);
