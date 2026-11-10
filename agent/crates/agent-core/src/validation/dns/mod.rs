//! Values written into a DNS zone file, and the names the daemon addresses a zone by.
//!
//! A zone file is a line-oriented structured file with directives (`$INCLUDE`,
//! `$GENERATE`, `$ORIGIN`, `$TTL`) and `;` comments, so the rule of
//! rules/security.md item 4 applies to every value here: a newline, a carriage
//! return, a NUL or any other control character, a quote, a backslash or a
//! `$` must never reach the file. Every type in this group is a constructor that
//! can fail and whose alphabet is closed — the value is validated, never
//! escaped — and there is deliberately no "raw record" or "raw rdata" type.
//!
//! `zone_name` and `zone_file_name` name a zone; `record_name` and `dns_name`
//! are the two kinds of name a record carries (an owner relative to its zone,
//! and a fully qualified target); `dns_ttl`, `soa_interval` and `zone_serial`
//! are the numbers of a zone's header; `record_priority`, `srv_weight`,
//! `caa_tag`, `caa_flags`, `caa_value` and `txt_text` are the rdata fields of
//! the record types the panel offers. `first_refused_text_character` is the one
//! alphabet the two free-text kinds share.
//!
//! `dnssec_key_material` is the signing key of a signed zone (ECDSA P-256, the
//! private half behind a `SecretString`, the key tag recomputed and never
//! trusted) and `dnssec_key_file_name` the names its files are written under,
//! built from a zone and that recomputed tag (issue #452).
//!
//! An address (`A`, `AAAA`) is `std::net::Ipv4Addr` / `Ipv6Addr` and an SRV port
//! is [`crate::validation::web::port::Port`]: both already are the validated type
//! and a second one would be a copy.

pub mod caa_flags;
pub mod caa_flags_error;
pub mod caa_tag;
pub mod caa_tag_error;
pub mod caa_value;
pub mod caa_value_error;
pub mod dns_name;
pub mod dns_name_error;
pub mod dns_ttl;
pub mod dns_ttl_error;
pub mod dnssec_key_file_name;
pub mod dnssec_key_file_name_error;
pub mod dnssec_key_material;
pub mod dnssec_key_material_error;
pub(crate) mod first_refused_text_character;
pub(crate) mod label_fault;
pub(crate) mod label_fault_of;
pub mod record_name;
pub mod record_name_error;
pub mod record_priority;
pub mod record_priority_error;
pub mod soa_interval;
pub mod soa_interval_error;
pub mod srv_weight;
pub mod srv_weight_error;
pub mod txt_text;
pub mod txt_text_error;
pub mod zone_file_name;
pub mod zone_file_name_error;
pub mod zone_name;
pub mod zone_name_error;
pub mod zone_serial;
pub mod zone_serial_error;
