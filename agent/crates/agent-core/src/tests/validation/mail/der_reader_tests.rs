//! Tests for the `der_reader` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::DerReader;

#[test]
fn a_short_form_element_is_read_and_the_reader_advances() {
    let mut reader = DerReader::new(&[0x02, 0x01, 0x07, 0x05, 0x00]);

    assert_eq!(reader.read(DerReader::INTEGER), Some(&[0x07][..]));
    assert_eq!(reader.read(DerReader::NULL), Some(&[][..]));
    assert!(reader.is_empty());
}

#[test]
fn a_long_form_element_is_read() {
    let mut bytes = vec![0x04, 0x81, 0x80];
    bytes.extend(vec![0xAB; 0x80]);
    let mut reader = DerReader::new(&bytes);

    assert_eq!(
        reader.read(DerReader::OCTET_STRING).map(<[u8]>::len),
        Some(0x80)
    );
    assert!(reader.is_empty());
}

#[test]
fn a_wrong_tag_returns_none_and_does_not_advance() {
    let mut reader = DerReader::new(&[0x02, 0x01, 0x07]);

    assert_eq!(reader.read(DerReader::SEQUENCE), None);
    assert_eq!(reader.read(DerReader::INTEGER), Some(&[0x07][..]));
}

#[test]
fn a_long_form_that_could_have_been_short_is_refused() {
    let mut reader = DerReader::new(&[0x04, 0x81, 0x05, 1, 2, 3, 4, 5]);

    assert_eq!(reader.read(DerReader::OCTET_STRING), None);
}

#[test]
fn a_length_with_a_leading_zero_byte_is_refused() {
    let mut bytes = vec![0x04, 0x82, 0x00, 0x80];
    bytes.extend(vec![0; 0x80]);

    assert_eq!(DerReader::new(&bytes).read(DerReader::OCTET_STRING), None);
}

#[test]
fn an_indefinite_or_oversized_length_form_is_refused() {
    assert_eq!(
        DerReader::new(&[0x30, 0x80, 0x00, 0x00]).read(DerReader::SEQUENCE),
        None
    );
    assert_eq!(
        DerReader::new(&[0x30, 0x84, 0x00, 0x00, 0x00, 0x01, 0x00]).read(DerReader::SEQUENCE),
        None
    );
}

#[test]
fn a_length_beyond_the_input_and_a_truncated_header_are_refused() {
    assert_eq!(
        DerReader::new(&[0x02, 0x05, 0x01]).read(DerReader::INTEGER),
        None
    );
    assert_eq!(DerReader::new(&[0x02]).read(DerReader::INTEGER), None);
    assert_eq!(
        DerReader::new(&[0x02, 0x82, 0x01]).read(DerReader::INTEGER),
        None
    );
    assert_eq!(DerReader::new(&[]).read(DerReader::INTEGER), None);
}
