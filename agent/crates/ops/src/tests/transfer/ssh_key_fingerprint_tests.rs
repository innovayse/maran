//! Tests for [`ssh_key_fingerprint`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::host_key::HostKey;

use super::ssh_key_fingerprint;
use crate::transfer::transfer_fixtures::{
    HOST_ECDSA_256, HOST_ECDSA_256_FINGERPRINT, HOST_ED25519, HOST_ED25519_FINGERPRINT, HOST_RSA,
    HOST_RSA_FINGERPRINT, PUBLIC_KEY_FINGERPRINT, PUBLIC_KEY_LINE,
};

#[test]
fn the_fingerprint_equals_the_one_ssh_keygen_prints_for_each_algorithm() {
    // The expected values are the ones the key's publisher states or
    // `ssh-keygen -l` printed when the fixture was made; the code under test
    // does not produce them.
    for (line, expected) in [
        (HOST_ED25519, HOST_ED25519_FINGERPRINT),
        (HOST_ECDSA_256, HOST_ECDSA_256_FINGERPRINT),
        (HOST_RSA, HOST_RSA_FINGERPRINT),
        (PUBLIC_KEY_LINE, PUBLIC_KEY_FINGERPRINT),
    ] {
        let key = HostKey::parse(line).unwrap();

        assert_eq!(ssh_key_fingerprint(&key).as_str(), expected);
    }
}

#[test]
fn two_different_keys_have_different_fingerprints() {
    assert_ne!(
        ssh_key_fingerprint(&HostKey::parse(HOST_ED25519).unwrap()),
        ssh_key_fingerprint(&HostKey::parse(PUBLIC_KEY_LINE).unwrap())
    );
}
