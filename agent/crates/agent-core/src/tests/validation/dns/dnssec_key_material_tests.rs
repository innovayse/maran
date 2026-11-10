//! Tests for the `dnssec_key_material` module.
//!
//! The key-tag vectors were derived independently of the code under test: a
//! short Python transcription of RFC 4034 appendix B (the DNSKEY RDATA of flags,
//! protocol 3, algorithm 13 and the key, summed as big-endian 16-bit words, the
//! carry folded once), written twice as a byte loop and as a word sum with the
//! same answers. They are NOT the output of a DNS tool: no `dnssec-keygen` was
//! available when they were written, so the tag is UNOBSERVED against a daemon
//! here and the polygon suite is where `named` reads the same key.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::dnssec_key_material::DnssecKeyMaterial;
use super::super::dnssec_key_material_error::DnssecKeyMaterialError;
use crate::secret_string::SecretString;

/// Base64 of the 64 bytes `0, 1, 2, ... 63`.
const PUBLIC_ASCENDING: &str =
    "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==";
/// Its RFC 4034 appendix B tag with flags 257.
const TAG_ASCENDING: u32 = 59_409;

/// Base64 of 64 bytes of `0xFF`.
const PUBLIC_ALL_ONES: &str =
    "/////////////////////////////////////////////////////////////////////////////////////w==";
/// Its tag with flags 257.
const TAG_ALL_ONES: u32 = 1_038;

/// Base64 of the 64 bytes `(i * 7 + 3) mod 256`.
const PUBLIC_STRIDED: &str =
    "AwoRGB8mLTQ7QklQV15lbHN6gYiPlp2kq7K5wMfO1dzj6vH4/wYNFBsiKTA3PkVMU1phaG92fYSLkpmgp661vA==";
/// Its tag with flags 257.
const TAG_STRIDED: u32 = 37_500;

/// The tag of the ascending key were its flags 256: proves the flags are part of
/// the sum and a ZSK tag is not accepted as this key's.
const TAG_ASCENDING_FLAGS_256: u32 = 59_408;

/// Base64 of the 32 recognisable bytes `MARAN-PLANTED-SCALAR-0123456789!`.
const PLANTED_PRIVATE: &str = "TUFSQU4tUExBTlRFRC1TQ0FMQVItMDEyMzQ1Njc4OSE=";

/// The planted private half, wrapped.
fn private() -> SecretString {
    SecretString::new(PLANTED_PRIVATE.to_owned())
}

/// Parses the ascending vector with the given private half.
fn parse_with_private(private: &SecretString) -> Result<DnssecKeyMaterial, DnssecKeyMaterialError> {
    DnssecKeyMaterial::parse(PUBLIC_ASCENDING, private, 257, TAG_ASCENDING)
}

/// The accepting control: a valid key parses and answers what it was given.
#[test]
fn a_valid_key_is_accepted_and_reports_its_parts() {
    let key = DnssecKeyMaterial::parse(PUBLIC_ASCENDING, &private(), 257, TAG_ASCENDING).unwrap();

    assert_eq!(u32::from(key.key_tag()), TAG_ASCENDING);
    assert_eq!(key.public_key_base64(), PUBLIC_ASCENDING);
    assert_eq!(key.flags(), 257);
    assert_eq!(key.algorithm(), 13);
    assert_eq!(key.protocol(), 3);
    assert_eq!(key.expose_private_key_base64(), PLANTED_PRIVATE);
}

/// Three independent known answers for the RFC 4034 appendix B sum, so a wrong
/// fold, a wrong word order or a wrong header byte moves at least one.
#[test]
fn the_key_tag_matches_the_independently_derived_known_answers() {
    for (public, tag) in [
        (PUBLIC_ASCENDING, TAG_ASCENDING),
        (PUBLIC_ALL_ONES, TAG_ALL_ONES),
        (PUBLIC_STRIDED, TAG_STRIDED),
    ] {
        let key = DnssecKeyMaterial::parse(public, &private(), 257, tag).unwrap();

        assert_eq!(u32::from(key.key_tag()), tag);
    }
}

/// A tag that is off by one is refused: the tag is checked, not copied.
#[test]
fn a_key_tag_that_differs_from_the_computed_one_is_refused() {
    for wrong in [TAG_ASCENDING + 1, TAG_ASCENDING - 1, 0, TAG_ALL_ONES] {
        assert!(
            matches!(
                DnssecKeyMaterial::parse(PUBLIC_ASCENDING, &private(), 257, wrong),
                Err(DnssecKeyMaterialError::KeyTagMismatch)
            ),
            "tag {wrong} must be refused"
        );
    }
}

/// A tag that does not fit sixteen bits cannot be the computed one, and is not
/// truncated into one that is.
#[test]
fn a_key_tag_beyond_sixteen_bits_is_refused_not_truncated() {
    let wrapped = TAG_ASCENDING + 65_536;

    assert!(matches!(
        DnssecKeyMaterial::parse(PUBLIC_ASCENDING, &private(), 257, wrapped),
        Err(DnssecKeyMaterialError::KeyTagMismatch)
    ));
}

/// The tag of the key with flags 256 is not accepted for the 257 key.
#[test]
fn the_tag_of_a_zone_signing_key_is_not_accepted_for_the_combined_key() {
    assert!(matches!(
        DnssecKeyMaterial::parse(PUBLIC_ASCENDING, &private(), 257, TAG_ASCENDING_FLAGS_256),
        Err(DnssecKeyMaterialError::KeyTagMismatch)
    ));
}

/// Flags other than 257 are refused, whatever the tag says.
#[test]
fn flags_other_than_257_are_refused() {
    for flags in [0, 1, 256, 258, 385, 65_536, 65_793, u32::MAX] {
        assert!(
            matches!(
                DnssecKeyMaterial::parse(PUBLIC_ASCENDING, &private(), flags, TAG_ASCENDING),
                Err(DnssecKeyMaterialError::UnsupportedFlags)
            ),
            "flags {flags} must be refused"
        );
    }
}

/// A public key of 63 or 65 bytes is refused, as are the extremes.
#[test]
fn a_public_key_of_the_wrong_length_is_refused() {
    // 63 bytes, 65 bytes and 0 bytes of the ascending pattern, each base64.
    let sixty_three =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+";
    let sixty_five =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0A=";

    for public in [sixty_three, sixty_five, ""] {
        assert!(
            matches!(
                DnssecKeyMaterial::parse(public, &private(), 257, TAG_ASCENDING),
                Err(DnssecKeyMaterialError::PublicKeyWrongLength)
            ),
            "public key {public:?} must be refused for its length"
        );
    }
}

/// A public key too long to be decoded at all is a length refusal.
#[test]
fn an_enormous_public_key_is_refused_without_being_decoded() {
    let enormous = "A".repeat(1_000_000);

    assert!(matches!(
        DnssecKeyMaterial::parse(&enormous, &private(), 257, TAG_ASCENDING),
        Err(DnssecKeyMaterialError::PublicKeyWrongLength)
    ));
}

/// A private key of 31 or 33 bytes is refused, as is none.
#[test]
fn a_private_key_of_the_wrong_length_is_refused() {
    // 31, 33 and 0 bytes.
    for text in [
        "TUFSQU4tUExBTlRFRC1TQ0FMQVItMDEyMzQ1Njc4OQ==",
        "TUFSQU4tUExBTlRFRC1TQ0FMQVItMDEyMzQ1Njc4OSEh",
        "",
    ] {
        assert!(
            matches!(
                parse_with_private(&SecretString::new(text.to_owned())),
                Err(DnssecKeyMaterialError::PrivateKeyWrongLength)
            ),
            "a private key of {} characters must be refused for its length",
            text.len()
        );
    }
}

/// Text outside the standard base64 alphabet is refused for each half.
#[test]
fn text_that_is_not_standard_base64_is_refused() {
    let url_safe = PUBLIC_ASCENDING.replace('+', "-");
    let not_base64 = "!".repeat(88);

    assert!(matches!(
        DnssecKeyMaterial::parse(&not_base64, &private(), 257, TAG_ASCENDING),
        Err(DnssecKeyMaterialError::PublicKeyNotBase64)
    ));
    assert!(matches!(
        DnssecKeyMaterial::parse(&url_safe, &private(), 257, TAG_STRIDED),
        Err(DnssecKeyMaterialError::PublicKeyNotBase64)
    ));
    assert!(matches!(
        parse_with_private(&SecretString::new("*".repeat(44))),
        Err(DnssecKeyMaterialError::PrivateKeyNotBase64)
    ));
}

/// Base64 with its padding removed is not canonical and is refused.
#[test]
fn unpadded_base64_is_refused() {
    let unpadded = PUBLIC_ASCENDING.trim_end_matches('=');

    assert!(matches!(
        DnssecKeyMaterial::parse(unpadded, &private(), 257, TAG_ASCENDING),
        Err(DnssecKeyMaterialError::PublicKeyNotBase64)
    ));
}

/// A newline, a carriage return, a tab, a space, a NUL or any other control
/// character in either half is refused as such, before any decoding: the private
/// half is written into a line-oriented file.
#[test]
fn whitespace_and_control_characters_are_refused_in_both_halves() {
    for hostile in ["\n", "\r\n", "\t", " ", "\0", "\u{7f}", "\u{1b}", "\u{85}"] {
        let public = format!("{PUBLIC_ASCENDING}{hostile}");
        assert!(
            matches!(
                DnssecKeyMaterial::parse(&public, &private(), 257, TAG_ASCENDING),
                Err(DnssecKeyMaterialError::PublicKeyControlCharacter)
            ),
            "{hostile:?} after the public key must be refused"
        );

        let embedded = format!(
            "{}{hostile}{}",
            &PUBLIC_ASCENDING[..40],
            &PUBLIC_ASCENDING[40..]
        );
        assert!(
            matches!(
                DnssecKeyMaterial::parse(&embedded, &private(), 257, TAG_ASCENDING),
                Err(DnssecKeyMaterialError::PublicKeyControlCharacter)
            ),
            "{hostile:?} inside the public key must be refused"
        );

        let secret = SecretString::new(format!("{PLANTED_PRIVATE}{hostile}"));
        assert!(
            matches!(
                parse_with_private(&secret),
                Err(DnssecKeyMaterialError::PrivateKeyControlCharacter)
            ),
            "{hostile:?} after the private key must be refused"
        );
    }
}

/// The accepting control for the sweep above: the same texts without the
/// hostile character parse.
#[test]
fn the_same_texts_without_a_control_character_are_accepted() {
    assert!(parse_with_private(&private()).is_ok());
}

/// The private half never appears in `Debug`, in an error, or in the formatted
/// error: a planted recognisable value is searched for, and the control proves
/// the search could find it.
#[test]
fn the_private_key_never_appears_in_debug_or_in_an_error() {
    let key = parse_with_private(&private()).unwrap();
    let printed = format!("{key:?} {key:#?}");

    // The search can see a planted value: the plain text contains it.
    assert!(key.expose_private_key_base64().contains("TUFSQU4tUExBTlRF"));
    assert!(PLANTED_PRIVATE.contains("TUFSQU4tUExBTlRF"));

    assert!(!printed.contains("TUFSQU4tUExBTlRF"));
    assert!(!printed.contains("MARAN-PLANTED"));
    assert!(printed.contains("«redacted»"));
    assert!(printed.contains(PUBLIC_ASCENDING));

    for refused in [
        DnssecKeyMaterial::parse(PUBLIC_ASCENDING, &private(), 257, 1),
        DnssecKeyMaterial::parse(PUBLIC_ASCENDING, &private(), 1, TAG_ASCENDING),
        DnssecKeyMaterial::parse("x", &private(), 257, TAG_ASCENDING),
        DnssecKeyMaterial::parse(
            PUBLIC_ASCENDING,
            &SecretString::new(format!("{PLANTED_PRIVATE}\n")),
            257,
            TAG_ASCENDING,
        ),
    ] {
        let error = refused.unwrap_err();
        let text = format!("{error} {error:?}");

        assert!(!text.contains("TUFSQU4tUExBTlRF"));
        assert!(!text.contains("MARAN-PLANTED"));
    }
}

/// A clone carries the same key and still prints no private half.
#[test]
fn a_clone_is_the_same_key_and_prints_no_private_half() {
    let key = parse_with_private(&private()).unwrap();
    let copy = key.clone();

    assert_eq!(copy.key_tag(), key.key_tag());
    assert_eq!(
        copy.expose_private_key_base64(),
        key.expose_private_key_base64()
    );
    assert!(!format!("{copy:?}").contains("TUFSQU4tUExBTlRF"));
}
