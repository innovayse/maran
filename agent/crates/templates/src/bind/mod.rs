//! Render types for the DNS daemon's configuration: `named.conf`, a zone file, and the zone statement
//! `rndc addzone` and `rndc modzone` are given, and the three DNSSEC texts (issue #452): the key's two files and
//! the probe configuration.

pub mod dnssec_private_key;
pub mod dnssec_probe;
pub mod dnssec_public_key;
pub mod named_conf;
pub mod zone_file;
pub mod zone_record_line;
pub mod zone_statement;
