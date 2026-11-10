//! Tests for [`parse_scanned_host_keys`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_scanned_host_keys;
use crate::transfer::transfer_fixtures::{
    HOST_ECDSA_256, HOST_ECDSA_256_FINGERPRINT, HOST_ECDSA_384, HOST_ED25519,
    HOST_ED25519_FINGERPRINT, HOST_RSA, HOST_RSA_FINGERPRINT,
};

/// What `ssh-keyscan` printed for a host offering three keys, rsa first.
fn scan_output() -> String {
    format!(
        "# 198.51.100.20:22 SSH-2.0-OpenSSH_9.6\n198.51.100.20 {HOST_RSA}\n# 198.51.100.20:22 SSH-2.0-OpenSSH_9.6\n198.51.100.20 {HOST_ECDSA_256}\n198.51.100.20 {HOST_ED25519}\n"
    )
}

#[test]
fn keys_come_back_in_the_agents_order_with_computed_fingerprints() {
    let keys = parse_scanned_host_keys(&scan_output());

    let seen: Vec<(&str, &str)> = keys
        .iter()
        .map(|key| (key.key.algorithm(), key.fingerprint.as_str()))
        .collect();
    assert_eq!(
        seen,
        vec![
            ("ssh-ed25519", HOST_ED25519_FINGERPRINT),
            ("ecdsa-sha2-nistp256", HOST_ECDSA_256_FINGERPRINT),
            ("ssh-rsa", HOST_RSA_FINGERPRINT),
        ]
    );
}

#[test]
fn a_nonstandard_port_host_field_is_not_trusted_or_used() {
    let output = format!("[198.51.100.20]:2222 {HOST_ED25519}\n");

    assert_eq!(parse_scanned_host_keys(&output).len(), 1);
}

#[test]
fn a_second_key_of_the_same_algorithm_is_dropped() {
    let output = format!("a {HOST_ED25519}\nb {HOST_ED25519}\n");

    assert_eq!(parse_scanned_host_keys(&output).len(), 1);
}

#[test]
fn the_four_wire_algorithms_are_all_kept() {
    let output =
        format!("h {HOST_ED25519}\nh {HOST_ECDSA_256}\nh {HOST_ECDSA_384}\nh {HOST_RSA}\n");

    assert_eq!(parse_scanned_host_keys(&output).len(), 4);
}

#[test]
fn hostile_lines_are_skipped_and_never_repaired() {
    let giant = "x".repeat(5000);
    let output = format!(
        "{giant}\n\
         h ssh-ed25519\n\
         h ssh-ed25519 not*base64\n\
         h ssh-dss AAAAB3NzaC1kc3MAAACB\n\
         h {HOST_ED25519} trailing\n\
         \n\
         # comment {HOST_ED25519}\n\
         h {HOST_ED25519}\n"
    );

    let keys = parse_scanned_host_keys(&output);

    assert_eq!(keys.len(), 1, "exactly the one well-formed line is kept");
    assert_eq!(keys[0].key.as_str(), HOST_ED25519);
}

#[test]
fn nothing_printed_is_nothing_found() {
    assert!(parse_scanned_host_keys("").is_empty());
    assert!(parse_scanned_host_keys("# only a banner\n").is_empty());
}
