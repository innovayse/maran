namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>The DS record a zone's owner publishes at the registrar so resolvers can chain trust to the zone's key.</summary>
/// <param name="KeyTag">The key tag of the DNSKEY the record names.</param>
/// <param name="Algorithm">The DNSSEC algorithm number of that key.</param>
/// <param name="DigestType">The digest algorithm number: 2 for SHA-256.</param>
/// <param name="Digest">The digest as upper-case hexadecimal.</param>
public sealed record DsRecord(int KeyTag, int Algorithm, int DigestType, string Digest);
