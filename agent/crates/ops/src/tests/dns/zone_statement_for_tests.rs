//! The statement the daemon is given for a zone it loads.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::zone_statement_for;
use crate::dns::model::signing_mode::SigningMode;

#[test]
fn the_unsigned_statement_is_byte_identical_to_the_text_formatted_before_signing_existed() {
    let old = format!(
        "{{ type primary; file \"{}\"; }};",
        "/var/lib/bind/maran/zones/example.com.db"
    );

    let new = zone_statement_for(
        Path::new("/var/lib/bind/maran/zones/example.com.db"),
        Path::new("/var/lib/bind/maran/keys"),
        SigningMode::Unsigned,
    )
    .unwrap();

    assert_eq!(new, old);
}

#[test]
fn a_signed_statement_names_the_key_directory_and_the_panels_policy() {
    let statement = zone_statement_for(
        Path::new("/s/example.com.db"),
        Path::new("/k"),
        SigningMode::Signed,
    )
    .unwrap();

    assert!(statement.contains("file \"/s/example.com.db\";"));
    assert!(statement.contains("key-directory \"/k\";"));
    assert!(statement.contains("dnssec-policy \"maran-csk\";"));
    assert!(statement.contains("inline-signing yes;"));
}

#[test]
fn a_withdrawing_statement_names_the_insecure_policy_and_not_the_panels() {
    let statement = zone_statement_for(
        Path::new("/s/example.com.db"),
        Path::new("/k"),
        SigningMode::Withdrawing,
    )
    .unwrap();

    assert!(statement.contains("dnssec-policy \"insecure\";"));
    assert!(!statement.contains("maran-csk"));
}

#[test]
fn an_unsigned_statement_never_mentions_the_key_directory() {
    let statement = zone_statement_for(
        Path::new("/z/example.com.db"),
        Path::new("/sentinel-keys"),
        SigningMode::Unsigned,
    )
    .unwrap();

    assert!(!statement.contains("sentinel-keys"));
    assert!(!statement.contains("dnssec-policy"));
}
