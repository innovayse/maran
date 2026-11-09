//! Tests for the `txt_text` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::txt_text::TxtText;
use super::super::txt_text_error::TxtTextError;

#[test]
fn spf_dkim_and_dmarc_values_are_accepted() {
    for ok in [
        "v=spf1 include:_spf.example.net ~all",
        "v=DMARC1; p=none; rua=mailto:d@example.com",
        "v=DKIM1; k=rsa; p=MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQC+/=",
    ] {
        assert_eq!(TxtText::parse(ok).unwrap().as_str(), ok);
    }
}

#[test]
fn a_quote_and_a_backslash_are_refused() {
    assert_eq!(
        TxtText::parse("a\"b"),
        Err(TxtTextError::IllegalCharacter { character: '"' })
    );
    assert_eq!(
        TxtText::parse("a\\b"),
        Err(TxtTextError::IllegalCharacter { character: '\\' })
    );
}

#[test]
fn control_characters_are_refused() {
    for hostile in ["a\nb", "a\rb", "a\0b", "a\tb", "a\u{7f}b", "caf\u{e9}"] {
        assert!(TxtText::parse(hostile).is_err(), "{hostile:?} was accepted");
    }
}

#[test]
fn a_directive_inside_the_text_is_inert_data_and_is_accepted() {
    // Between quotes a dollar sign and a semicolon are data to the zone parser.
    assert!(TxtText::parse("$INCLUDE /etc/passwd ; x").is_ok());
}

#[test]
fn empty_and_oversized_texts_are_refused() {
    assert_eq!(TxtText::parse(""), Err(TxtTextError::Empty));
    assert!(TxtText::parse(&"a".repeat(2048)).is_ok());
    assert_eq!(
        TxtText::parse(&"a".repeat(2049)),
        Err(TxtTextError::TooLong)
    );
}

#[test]
fn a_long_text_is_split_into_chunks_of_255_bytes() {
    let text = TxtText::parse(&"a".repeat(600)).unwrap();
    let lengths: Vec<usize> = text.chunks().iter().map(|c| c.len()).collect();

    assert_eq!(lengths, vec![255, 255, 90]);
    assert_eq!(text.chunks().concat(), text.as_str());
}

#[test]
fn a_short_text_is_one_chunk() {
    assert_eq!(TxtText::parse("hello").unwrap().chunks(), vec!["hello"]);
}
