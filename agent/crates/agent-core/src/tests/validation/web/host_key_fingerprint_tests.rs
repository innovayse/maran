//! Tests for the `host_key_fingerprint` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{HOST_KEY_DIGEST_BYTES, HostKeyFingerprint, HostKeyFingerprintError};

/// A well-formed fingerprint: 43 base64 characters whose last one carries only
/// zero padding bits.
const VALID: &str = "SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8";

#[test]
fn a_well_formed_fingerprint_is_accepted_and_kept_as_typed() {
    let fingerprint = HostKeyFingerprint::parse(VALID).unwrap();

    assert_eq!(fingerprint.as_str(), VALID);
}

#[test]
fn a_digest_round_trips_through_its_own_text() {
    let digest = [0xab_u8; HOST_KEY_DIGEST_BYTES];
    let made = HostKeyFingerprint::from_digest(&digest);

    assert!(made.as_str().starts_with("SHA256:"));
    assert_eq!(made.as_str().len(), "SHA256:".len() + 43);
    assert_eq!(HostKeyFingerprint::parse(made.as_str()).unwrap(), made);
}

#[test]
fn an_all_zero_digest_is_forty_three_a_characters() {
    let made = HostKeyFingerprint::from_digest(&[0_u8; HOST_KEY_DIGEST_BYTES]);

    assert_eq!(made.as_str(), format!("SHA256:{}", "A".repeat(43)));
}

#[test]
fn a_missing_or_other_prefix_is_refused() {
    for candidate in [
        "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8",
        "sha256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8",
        "MD5:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8",
        "SHA256nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8",
        "",
    ] {
        assert_eq!(
            HostKeyFingerprint::parse(candidate),
            Err(HostKeyFingerprintError::MissingPrefix),
            "{candidate:?}"
        );
    }
}

#[test]
fn a_wrong_length_is_refused() {
    assert_eq!(
        HostKeyFingerprint::parse("SHA256:"),
        Err(HostKeyFingerprintError::WrongLength)
    );
    assert_eq!(
        HostKeyFingerprint::parse(&VALID[..VALID.len() - 1]),
        Err(HostKeyFingerprintError::WrongLength)
    );
    assert_eq!(
        HostKeyFingerprint::parse(&format!("{VALID}A")),
        Err(HostKeyFingerprintError::WrongLength)
    );
}

#[test]
fn a_padded_spelling_is_refused() {
    // 43 characters including a `=`: the right length, the wrong alphabet.
    let padded = format!("SHA256:{}=", "A".repeat(42));

    assert_eq!(
        HostKeyFingerprint::parse(&padded),
        Err(HostKeyFingerprintError::InvalidEncoding)
    );
}

#[test]
fn the_url_safe_alphabet_is_refused() {
    let url_safe = format!("SHA256:{}-", "A".repeat(42));

    assert_eq!(
        HostKeyFingerprint::parse(&url_safe),
        Err(HostKeyFingerprintError::InvalidEncoding)
    );
}

#[test]
fn non_zero_bits_after_the_digest_are_refused() {
    // `B` as the 43rd character sets a bit below the 256 digest bits.
    let sloppy = format!("SHA256:{}B", "A".repeat(42));

    assert_eq!(
        HostKeyFingerprint::parse(&sloppy),
        Err(HostKeyFingerprintError::InvalidEncoding)
    );
    // The inverse control: `A` in the same place is the canonical spelling.
    assert!(HostKeyFingerprint::parse(&format!("SHA256:{}A", "A".repeat(42))).is_ok());
}

#[test]
fn whitespace_and_control_characters_are_refused() {
    for suffix in ["\n", " ", "\0"] {
        let candidate = format!("{VALID}{suffix}");

        assert!(
            HostKeyFingerprint::parse(&candidate).is_err(),
            "{candidate:?}"
        );
    }
    let inside = VALID.replacen('b', " ", 1);
    assert!(HostKeyFingerprint::parse(&inside).is_err());
}
