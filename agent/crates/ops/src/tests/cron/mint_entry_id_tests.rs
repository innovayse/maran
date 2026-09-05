//! What the entry-id minter produces, and what the validated type makes of it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;

use super::{format_uuid, mint_entry_id};

/// Sixteen bytes with every nibble distinguishable, so a transposition shows.
const BYTES: [u8; 16] = [
    0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x46, 0x77, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff,
];

/// The bytes are written in order, lowercase, with hyphens at 8-4-4-4-12.
#[test]
fn the_bytes_are_written_as_a_lowercase_hyphenated_uuid() {
    assert_eq!(format_uuid(&BYTES), "00112233-4455-4677-8899-aabbccddeeff");
}

/// What the minter formats is what the validated type accepts.
#[test]
fn a_formatted_uuid_is_one_the_entry_id_type_accepts() {
    // The type is the only thing standing between an id and a filesystem path,
    // so a minter whose output it refused would be a defect that only showed up
    // as a failed creation.
    CronEntryId::parse(&format_uuid(&BYTES)).expect("the minter's own shape is accepted");
}

/// A minted id parses, and carries the version and variant of a uuid.
#[test]
fn a_minted_id_is_a_well_formed_version_four_uuid() {
    let id = mint_entry_id().expect("the kernel has a randomness source");
    let text = id.as_str();

    assert_eq!(text.len(), 36);
    assert_eq!(
        text.chars().nth(14),
        Some('4'),
        "the version nibble is set: {text}"
    );
    assert!(
        matches!(text.chars().nth(19), Some('8' | '9' | 'a' | 'b')),
        "the variant bits are set: {text}"
    );
}

/// Two mintings do not produce the same id.
#[test]
fn two_minted_ids_differ() {
    // The id names three files; two entries sharing one would share their
    // command, their output and their exit status.
    let first = mint_entry_id().expect("an id");
    let second = mint_entry_id().expect("an id");

    assert_ne!(first, second);
}
