//! How many entries a zip file says its central directory holds, read before the zip crate holds them.

use std::fs::File;
use std::os::unix::fs::FileExt;

/// Signature of the end-of-central-directory record.
const END_RECORD: [u8; 4] = [b'P', b'K', 5, 6];

/// Signature of the zip64 end-of-central-directory locator.
const LOCATOR: [u8; 4] = [b'P', b'K', 6, 7];

/// Signature of the zip64 end-of-central-directory record.
const ZIP64_END_RECORD: [u8; 4] = [b'P', b'K', 6, 6];

/// Size of the end-of-central-directory record without its comment.
const END_RECORD_LENGTH: u64 = 22;

/// Size of the zip64 locator.
const LOCATOR_LENGTH: u64 = 20;

/// Size of the zip64 end-of-central-directory record without its extensible data.
const ZIP64_END_RECORD_LENGTH: u64 = 56;

/// Reads the entry count a zip file DECLARES, without parsing the directory.
///
/// The zip crate reads the whole central directory into memory before it hands
/// back a single entry, so a file that declares millions of entries costs that
/// memory before any cap can be applied to them. This asks the one question the
/// cap needs, from the last few kilobytes of the file, so the walk can refuse
/// first. The record is found the way a conforming reader finds it: the last
/// signature whose comment length lands exactly on the end of the file.
///
/// `None` means the structure was not recognisable (no record, a zip64 locator
/// pointing outside the file). That is not an answer to rely on: the walk still
/// counts every entry it reads, and the zip crate still rejects what it cannot
/// parse. This is an early refusal, not the only one.
pub(crate) fn zip_central_entry_count(file: &File, length: u64) -> Option<u64> {
    let tail_length = length.min(u64::from(u16::MAX) + END_RECORD_LENGTH);
    if tail_length < END_RECORD_LENGTH {
        return None;
    }
    let tail_start = length - tail_length;
    let mut tail = vec![0_u8; usize::try_from(tail_length).ok()?];
    file.read_exact_at(&mut tail, tail_start).ok()?;

    let last_start = tail.len() - usize::try_from(END_RECORD_LENGTH).ok()?;
    let position = (0..=last_start).rev().find(|&at| {
        tail[at..at + 4] == END_RECORD && {
            let comment = usize::from(u16::from_le_bytes([tail[at + 20], tail[at + 21]]));
            at + 22 + comment == tail.len()
        }
    })?;

    let entries = u64::from(u16::from_le_bytes([
        tail[position + 10],
        tail[position + 11],
    ]));
    if entries != u64::from(u16::MAX) {
        return Some(entries);
    }

    // 0xFFFF is the marker for "look in the zip64 record".
    let end_record_at = tail_start + u64::try_from(position).ok()?;
    let locator_at = end_record_at.checked_sub(LOCATOR_LENGTH)?;
    let mut locator = [0_u8; 20];
    file.read_exact_at(&mut locator, locator_at).ok()?;
    if locator[..4] != LOCATOR {
        return None;
    }
    let record_at = u64::from_le_bytes(locator[8..16].try_into().ok()?);
    if record_at.checked_add(ZIP64_END_RECORD_LENGTH)? > length {
        return None;
    }
    let mut record = [0_u8; 56];
    file.read_exact_at(&mut record, record_at).ok()?;
    if record[..4] != ZIP64_END_RECORD {
        return None;
    }
    Some(u64::from_le_bytes(record[32..40].try_into().ok()?))
}

#[cfg(test)]
#[path = "../../tests/files/archive/zip_central_entry_count_tests.rs"]
mod tests;
