//! One record line of a zone file.

/// One line of a zone file below the SOA and the apex NS set: owner, TTL, type
/// and the text of the data.
///
/// The fields are text because this crate depends on nothing that validates; the
/// caller builds each from validated types (`maran-ops`, `dns::record_rdata`) and
/// nothing a request carried reaches a field any other way (rules/security.md
/// item 4). The line is `owner ttl IN type rdata`, so the owner is always the
/// first token and cannot be read as a TTL or a class.
pub struct ZoneRecordLine<'a> {
    /// The owner column: `@` or labels relative to the origin.
    pub owner: &'a str,
    /// The TTL in seconds.
    pub ttl: u32,
    /// The type mnemonic: `A`, `AAAA`, `CNAME`, `MX`, `TXT`, `SRV`, `CAA`, `NS`.
    pub record_type: &'a str,
    /// The data, already in zone-file form (quoted where the type needs it).
    pub rdata: &'a str,
}
