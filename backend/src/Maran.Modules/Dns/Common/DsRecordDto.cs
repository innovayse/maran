namespace Maran.Modules.Dns.Common;

/// <summary>The DS record a zone's owner publishes at the registrar, in the fields registrars ask for.</summary>
/// <param name="KeyTag">The key tag of the zone's key.</param>
/// <param name="Algorithm">The DNSSEC algorithm number of the key: 13 for ECDSA P-256.</param>
/// <param name="DigestType">The digest algorithm number: 2 for SHA-256.</param>
/// <param name="Digest">The digest as upper-case hexadecimal.</param>
public sealed record DsRecordDto(int KeyTag, int Algorithm, int DigestType, string Digest);
