//! Minting the id that names an entry's three files.

use std::fs::File;
use std::io::Read as _;

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;

use crate::cron::cron_error::CronError;

/// The kernel's randomness source.
///
/// Not a platform fact and not a `DistroAdapter` question: it is a kernel
/// interface with one spelling on every system this panel supports, the same
/// class of path as `/proc`. What differs between families — where `crontab`
/// lives, which shell `/bin/sh` is — is asked of the adapter, and none of it is
/// here.
const RANDOM_SOURCE: &str = "/dev/urandom";

/// How many bytes a uuid is built from.
const UUID_BYTES: usize = 16;

/// The byte positions a hyphen is written before, forming `8-4-4-4-12`.
const HYPHEN_BEFORE: [usize; 4] = [4, 6, 8, 10];

/// The lowercase alphabet the hexadecimal is written in.
///
/// A table rather than a `{:02x}` format call, so the function allocates one
/// string and nothing else — and so it has no formatting machinery between the
/// bytes and the text a filesystem path is built from.
const HEX_DIGITS: [u8; 16] = *b"0123456789abcdef";

/// Mints a version 4 uuid for a new cron entry.
///
/// The id names three files under a customer's home, so it is handed to
/// [`CronEntryId::parse`] rather than wrapped: that type is the only thing
/// standing between an id and a path, and a value minted here is checked by it
/// exactly like one that arrived from anywhere else (rules/rust.md "Validation
/// first"). A minter whose output the type refused would be a bug caught at the
/// moment it happened rather than a path built from something unexpected.
///
/// The version and variant nibbles are set so the value is a well-formed uuid
/// rather than sixteen random bytes wearing its shape. [`CronEntryId`]
/// deliberately does not check them — it answers "can this safely be a path
/// segment" — so setting them here is what makes the ids this agent writes
/// recognisable as uuids to everything that later reads them.
///
/// # Errors
///
/// Returns [`CronError::EntryIdUnavailable`] when the randomness source cannot
/// be read, or when what it produced is somehow not an id this agent would
/// accept.
pub(crate) fn mint_entry_id() -> Result<CronEntryId, CronError> {
    let mut bytes = [0u8; UUID_BYTES];
    File::open(RANDOM_SOURCE)
        .and_then(|mut source| source.read_exact(&mut bytes))
        .map_err(|_| CronError::EntryIdUnavailable)?;

    // Version 4 in the high nibble of byte 6, and the RFC 4122 variant in the
    // top two bits of byte 8.
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;

    CronEntryId::parse(&format_uuid(&bytes)).map_err(|_| CronError::EntryIdUnavailable)
}

/// Writes `bytes` as a lowercase hyphenated uuid.
///
/// Split from the minting above so the formatting can be driven with fixed
/// bytes by a test: the source of the bytes is the one part of this file that
/// cannot be made deterministic, and it is also the part with no decision in
/// it.
fn format_uuid(bytes: &[u8; UUID_BYTES]) -> String {
    let mut text = String::with_capacity(UUID_BYTES * 2 + HYPHEN_BEFORE.len());

    for (position, byte) in bytes.iter().enumerate() {
        if HYPHEN_BEFORE.contains(&position) {
            text.push('-');
        }
        // Both indices are below 16 by construction, so the lookups cannot be
        // out of bounds and this function has no panicking path.
        text.push(char::from(HEX_DIGITS[usize::from(byte >> 4)]));
        text.push(char::from(HEX_DIGITS[usize::from(byte & 0x0f)]));
    }

    text
}

#[cfg(test)]
#[path = "../tests/cron/mint_entry_id_tests.rs"]
mod tests;
