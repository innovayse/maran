//! What a `PutZone` request's signing instruction carries, revalidated.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::dns::{SigningMode, ZoneSigning};

use super::validated_signing;
use crate::proto::{DnsSigning, DnsSigningKey, DnsSigningMode, ErrorCode};

/// Base64 of the 64 bytes `0, 1, 2, ... 63`.
const PUBLIC: &str =
    "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==";

/// Its RFC 4034 tag with flags 257.
const TAG: u32 = 59_409;

/// Base64 of the 32 recognisable bytes `MARAN-PLANTED-SCALAR-0123456789!`.
const PLANTED_PRIVATE: &str = "TUFSQU4tUExBTlRFRC1TQ0FMQVItMDEyMzQ1Njc4OSE=";

/// A valid key, to be varied by a test.
fn key() -> DnsSigningKey {
    DnsSigningKey {
        public_key: PUBLIC.to_owned(),
        private_key: PLANTED_PRIVATE.to_owned(),
        flags: 257,
        key_tag: TAG,
    }
}

/// An instruction in `mode` carrying `key`.
fn instruction(mode: DnsSigningMode, key: Option<DnsSigningKey>) -> DnsSigning {
    DnsSigning {
        mode: mode as i32,
        key,
    }
}

#[test]
fn an_absent_instruction_is_an_unsigned_zone() {
    assert!(matches!(validated_signing(None), Ok(ZoneSigning::Unsigned)));
}

#[test]
fn an_unsigned_instruction_without_a_key_is_an_unsigned_zone() {
    let signing = validated_signing(Some(&instruction(DnsSigningMode::Unsigned, None))).unwrap();

    assert_eq!(signing.mode(), SigningMode::Unsigned);
}

#[test]
fn a_key_for_an_unsigned_zone_is_refused() {
    let refused =
        validated_signing(Some(&instruction(DnsSigningMode::Unsigned, Some(key())))).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_key_for_a_signed_zone_is_accepted() {
    // The inverse control of the refusal above: the same key, a mode that uses it.
    let signing =
        validated_signing(Some(&instruction(DnsSigningMode::Signed, Some(key())))).unwrap();

    assert_eq!(signing.mode(), SigningMode::Signed);
    assert_eq!(signing.key().unwrap().key_tag(), 59_409);
}

#[test]
fn a_key_for_a_withdrawing_zone_is_accepted() {
    let signing =
        validated_signing(Some(&instruction(DnsSigningMode::Withdrawing, Some(key())))).unwrap();

    assert_eq!(signing.mode(), SigningMode::Withdrawing);
}

#[test]
fn a_signed_or_withdrawing_zone_without_a_key_is_refused() {
    for mode in [DnsSigningMode::Signed, DnsSigningMode::Withdrawing] {
        let refused = validated_signing(Some(&instruction(mode, None))).unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32, "{mode:?}");
    }
}

#[test]
fn a_mode_number_the_enum_does_not_know_is_refused_and_not_read_as_unsigned() {
    let refused = validated_signing(Some(&DnsSigning {
        mode: 99,
        key: None,
    }))
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_wrong_key_tag_is_refused_and_the_right_one_accepted() {
    let wrong = DnsSigningKey {
        key_tag: TAG + 1,
        ..key()
    };

    assert!(validated_signing(Some(&instruction(DnsSigningMode::Signed, Some(wrong)))).is_err());
    assert!(validated_signing(Some(&instruction(DnsSigningMode::Signed, Some(key())))).is_ok());
}

#[test]
fn flags_other_than_257_are_refused() {
    let zone_signing_key = DnsSigningKey {
        flags: 256,
        ..key()
    };

    assert!(
        validated_signing(Some(&instruction(
            DnsSigningMode::Signed,
            Some(zone_signing_key)
        )))
        .is_err()
    );
}

#[test]
fn a_key_that_is_not_base64_or_not_the_right_length_is_refused() {
    let bad_public = DnsSigningKey {
        public_key: "not base64 !".to_owned(),
        ..key()
    };
    let short_private = DnsSigningKey {
        private_key: "AAAA".to_owned(),
        ..key()
    };

    for bad in [bad_public, short_private] {
        assert!(validated_signing(Some(&instruction(DnsSigningMode::Signed, Some(bad)))).is_err());
    }
}

#[test]
fn no_refusal_carries_the_planted_private_key() {
    // Positive control: the planted value is findable in the request, so a leak
    // into any message below would be findable by the same search.
    assert!(format!("{:?}", key()).contains(PLANTED_PRIVATE));

    let refusals = [
        validated_signing(Some(&instruction(DnsSigningMode::Unsigned, Some(key())))),
        validated_signing(Some(&instruction(
            DnsSigningMode::Signed,
            Some(DnsSigningKey {
                key_tag: TAG + 1,
                ..key()
            }),
        ))),
        validated_signing(Some(&instruction(
            DnsSigningMode::Signed,
            Some(DnsSigningKey {
                public_key: "x".to_owned(),
                ..key()
            }),
        ))),
        validated_signing(Some(&instruction(
            DnsSigningMode::Signed,
            Some(DnsSigningKey {
                private_key: format!("{PLANTED_PRIVATE}{PLANTED_PRIVATE}"),
                ..key()
            }),
        ))),
    ];

    for refusal in refusals {
        let error = refusal.unwrap_err();
        assert!(
            !error.message.contains(PLANTED_PRIVATE),
            "a refusal carried the key: {}",
            error.message
        );
        assert!(error.tool_output.is_empty());
    }
}

#[test]
fn the_accepted_zone_signing_never_prints_the_private_key() {
    let signing =
        validated_signing(Some(&instruction(DnsSigningMode::Signed, Some(key())))).unwrap();

    assert!(!format!("{signing:?}").contains(PLANTED_PRIVATE));
}
