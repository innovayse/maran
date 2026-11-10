//! Tests for the `host_key` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::{HostKey, HostKeyError};

/// One SSH wire-format string: a big-endian length and the bytes.
fn wire_string(bytes: &[u8]) -> Vec<u8> {
    let mut out = u32::try_from(bytes.len()).unwrap().to_be_bytes().to_vec();
    out.extend_from_slice(bytes);
    out
}

/// A key blob that names `algorithm` and carries 32 bytes of key material.
fn blob_of(algorithm: &str) -> Vec<u8> {
    let mut blob = wire_string(algorithm.as_bytes());
    blob.extend(wire_string(&[7_u8; 32]));
    blob
}

/// The `<algorithm> <base64>` text of a key of `algorithm`.
fn key_text(algorithm: &str) -> String {
    format!("{algorithm} {}", STANDARD.encode(blob_of(algorithm)))
}

#[test]
fn a_key_of_each_accepted_algorithm_is_accepted() {
    for algorithm in [
        "ssh-ed25519",
        "ecdsa-sha2-nistp256",
        "ecdsa-sha2-nistp384",
        "ecdsa-sha2-nistp521",
        "ssh-rsa",
    ] {
        let key = HostKey::parse(&key_text(algorithm)).unwrap();

        assert_eq!(key.algorithm(), algorithm);
        assert_eq!(key.as_str(), key_text(algorithm));
        assert_eq!(key.blob(), blob_of(algorithm).as_slice());
    }
}

#[test]
fn an_empty_candidate_is_refused() {
    assert_eq!(HostKey::parse(""), Err(HostKeyError::Empty));
}

#[test]
fn a_candidate_over_the_ceiling_is_refused_and_one_at_it_is_not_refused_for_length() {
    let over = format!("ssh-rsa {}", "A".repeat(2049));
    assert_eq!(HostKey::parse(&over), Err(HostKeyError::InvalidEncoding));

    let way_over = format!("ssh-rsa {}", "A".repeat(3000));
    assert_eq!(HostKey::parse(&way_over), Err(HostKeyError::TooLong));
}

#[test]
fn a_newline_or_any_control_character_is_refused_before_anything_is_split() {
    let good = key_text("ssh-ed25519");
    for injected in [
        format!("{good}\nssh-rsa AAAA"),
        format!("{good}\r"),
        format!("{good}\0"),
        format!("{good}\t"),
        format!("\n{good}"),
    ] {
        assert_eq!(
            HostKey::parse(&injected),
            Err(HostKeyError::ControlCharacter),
            "{injected:?}"
        );
    }
}

#[test]
fn a_comment_an_option_or_extra_spacing_is_refused() {
    let good = key_text("ssh-ed25519");
    for candidate in [
        format!("{good} root@box"),
        format!("{good} "),
        format!(" {good}"),
        format!(
            "{}  {}",
            "ssh-ed25519",
            STANDARD.encode(blob_of("ssh-ed25519"))
        ),
        format!("host.example.com {good}"),
        format!("@cert-authority {good}"),
    ] {
        assert!(HostKey::parse(&candidate).is_err(), "{candidate:?}");
    }
}

#[test]
fn a_single_field_is_refused() {
    assert_eq!(
        HostKey::parse("ssh-ed25519"),
        Err(HostKeyError::NotTwoFields)
    );
}

#[test]
fn an_algorithm_off_the_allow_list_is_refused() {
    let blob = STANDARD.encode(blob_of("ssh-dss"));
    for algorithm in [
        "ssh-dss",
        "sk-ssh-ed25519@openssh.com",
        "SSH-ED25519",
        "rsa",
    ] {
        assert_eq!(
            HostKey::parse(&format!("{algorithm} {blob}")),
            Err(HostKeyError::UnsupportedAlgorithm),
            "{algorithm}"
        );
    }
}

#[test]
fn a_blob_that_is_not_base64_is_refused() {
    for data in ["!!!!", "AAAA=AAA", "AAA", "AAAA\u{00a0}"] {
        assert!(
            HostKey::parse(&format!("ssh-ed25519 {data}")).is_err(),
            "{data:?}"
        );
    }
}

#[test]
fn a_blob_too_short_to_name_an_algorithm_is_refused() {
    let short = STANDARD.encode([0_u8, 0, 0]);
    assert_eq!(
        HostKey::parse(&format!("ssh-ed25519 {short}")),
        Err(HostKeyError::Malformed)
    );

    // A length that promises more bytes than the blob holds.
    let lying = STANDARD.encode([0_u8, 0, 0, 200, b's']);
    assert_eq!(
        HostKey::parse(&format!("ssh-ed25519 {lying}")),
        Err(HostKeyError::Malformed)
    );
}

#[test]
fn a_blob_naming_another_algorithm_than_the_first_field_is_refused() {
    let rsa_blob = STANDARD.encode(blob_of("ssh-rsa"));

    assert_eq!(
        HostKey::parse(&format!("ssh-ed25519 {rsa_blob}")),
        Err(HostKeyError::AlgorithmMismatch)
    );
    // The inverse control: the same blob under its own algorithm is accepted.
    assert!(HostKey::parse(&format!("ssh-rsa {rsa_blob}")).is_ok());
}
