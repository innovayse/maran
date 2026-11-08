//! The header of a sealed archive, and what nonces and associated data come of it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// A header with a prefix a test can recognise.
fn header() -> EncryptedArtifactHeader {
    EncryptedArtifactHeader::parse(&{
        let mut bytes = [0_u8; HEADER_BYTES];
        bytes[..8].copy_from_slice(b"MARANENC");
        bytes[8] = 1;
        for (offset, byte) in bytes[9..].iter_mut().enumerate() {
            *byte = 0xa0 + offset as u8;
        }
        bytes
    })
    .expect("the fixture header is well-formed")
}

#[test]
fn a_header_survives_being_written_and_read_back() {
    let written = header().to_bytes();

    let read = EncryptedArtifactHeader::parse(&written).expect("a written header parses");

    assert_eq!(read, header());
    assert_eq!(written.len(), HEADER_BYTES);
}

#[test]
fn a_wrong_magic_is_not_a_header() {
    let mut bytes = header().to_bytes();
    bytes[0] ^= 0xff;

    assert!(EncryptedArtifactHeader::parse(&bytes).is_none());
}

#[test]
fn an_unknown_format_version_is_not_a_header() {
    let mut bytes = header().to_bytes();
    bytes[8] = 2;

    assert!(EncryptedArtifactHeader::parse(&bytes).is_none());
}

#[test]
fn the_magic_is_recognised_in_a_prefix_and_not_in_a_gzip_stream() {
    assert!(EncryptedArtifactHeader::has_magic(&header().to_bytes()));
    assert!(EncryptedArtifactHeader::has_magic(b"MARANENC"));
    assert!(!EncryptedArtifactHeader::has_magic(&[
        0x1f, 0x8b, 0x08, 0, 0, 0, 0, 0
    ]));
    assert!(!EncryptedArtifactHeader::has_magic(b"MARANEN"));
    assert!(!EncryptedArtifactHeader::has_magic(b""));
}

#[test]
fn a_chunks_nonce_is_the_prefix_then_its_number_in_big_endian() {
    let nonce = header().nonce(0x0102_0304_0506_0708);

    assert_eq!(nonce.len(), 24);
    assert_eq!(nonce[..16], header().to_bytes()[9..]);
    assert_eq!(nonce[16..], [1, 2, 3, 4, 5, 6, 7, 8]);
}

#[test]
fn two_chunks_of_one_archive_never_share_a_nonce() {
    assert_ne!(header().nonce(0), header().nonce(1));
}

#[test]
fn the_last_chunk_flag_changes_the_associated_data_and_the_header_is_covered() {
    let ordinary = header().associated_data(false);
    let last = header().associated_data(true);

    assert_ne!(ordinary, last);
    assert_eq!(ordinary[..HEADER_BYTES], header().to_bytes());
    assert_eq!(ordinary[HEADER_BYTES], 0);
    assert_eq!(last[HEADER_BYTES], 1);
}

#[test]
fn two_generated_headers_have_different_prefixes() {
    // The property every nonce depends on. A positive control would be a
    // generator that repeats; a probability of 2^-128 is what this relies on.
    let first = EncryptedArtifactHeader::generate().expect("randomness is available");
    let second = EncryptedArtifactHeader::generate().expect("randomness is available");

    assert_ne!(first, second);
}
