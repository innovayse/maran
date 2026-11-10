//! Tests for a scan's answer on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::host_key::HostKey;
use maran_agent_core::validation::web::host_key_fingerprint::HostKeyFingerprint;
use maran_ops::transfer::ScannedHostKey;

use super::to_wire_scanned_host_keys;

/// An inert ed25519 host key.
const ED25519: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH";

fn scanned(text: &str, digest_byte: u8) -> ScannedHostKey {
    ScannedHostKey {
        key: HostKey::parse(text).unwrap(),
        fingerprint: HostKeyFingerprint::from_digest(&[digest_byte; 32]),
    }
}

#[test]
fn each_key_carries_its_own_algorithm_text_and_fingerprint_in_order() {
    let first = scanned(ED25519, 1);
    let second = scanned(ED25519, 2);

    let wire = to_wire_scanned_host_keys(&[first.clone(), second.clone()]);

    assert_eq!(wire.keys.len(), 2);
    assert_eq!(wire.keys[0].algorithm, "ssh-ed25519");
    assert_eq!(wire.keys[0].key, ED25519);
    assert_eq!(wire.keys[0].fingerprint, first.fingerprint.as_str());
    assert_eq!(wire.keys[1].fingerprint, second.fingerprint.as_str());
    assert_ne!(wire.keys[0].fingerprint, wire.keys[1].fingerprint);
}

#[test]
fn no_key_is_an_empty_list_and_not_an_invented_one() {
    assert!(to_wire_scanned_host_keys(&[]).keys.is_empty());
}
