#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

fn valid() -> String {
    "0123456789abcdef".repeat(4)
}

#[test]
fn a_64_character_lowercase_hex_key_is_accepted_and_decoded() {
    let key = ArchiveKey::parse(&valid()).expect("a well-formed key parses");

    assert_eq!(key.expose()[0], 0x01);
    assert_eq!(key.expose()[1], 0x23);
    assert_eq!(key.expose()[7], 0xef);
    assert_eq!(key.expose().len(), ARCHIVE_KEY_BYTES);
}

#[test]
fn an_empty_key_is_refused_as_the_wrong_length() {
    assert_eq!(
        ArchiveKey::parse("").err(),
        Some(ArchiveKeyError::WrongLength)
    );
}

#[test]
fn a_key_one_character_short_or_long_is_refused_as_the_wrong_length() {
    let short = &valid()[..63];
    let long = format!("{}0", valid());

    assert_eq!(
        ArchiveKey::parse(short).err(),
        Some(ArchiveKeyError::WrongLength)
    );
    assert_eq!(
        ArchiveKey::parse(&long).err(),
        Some(ArchiveKeyError::WrongLength)
    );
}

#[test]
fn an_uppercase_digit_is_refused() {
    let mut candidate = valid();
    candidate.replace_range(0..1, "A");

    assert_eq!(
        ArchiveKey::parse(&candidate).err(),
        Some(ArchiveKeyError::NotLowercaseHex)
    );
}

#[test]
fn a_non_hex_character_is_refused_even_where_a_hex_digit_pair_is_expected() {
    let mut candidate = valid();
    candidate.replace_range(10..11, "g");

    assert_eq!(
        ArchiveKey::parse(&candidate).err(),
        Some(ArchiveKeyError::NotLowercaseHex)
    );
}

#[test]
fn a_multibyte_character_is_refused_without_a_panic() {
    // 62 ASCII bytes plus one two-byte character: 64 BYTES, so the length test
    // passes and the digit test is what must refuse it.
    let candidate = format!("{}é", "a".repeat(62));
    assert_eq!(candidate.len(), 64);

    assert_eq!(
        ArchiveKey::parse(&candidate).err(),
        Some(ArchiveKeyError::NotLowercaseHex)
    );
}

#[test]
fn the_debug_form_never_shows_the_key() {
    let key = ArchiveKey::parse(&valid()).expect("a well-formed key parses");

    let rendered = format!("{key:?}");

    assert_eq!(rendered, "«redacted»");
    assert!(!rendered.contains("0123"));
}
