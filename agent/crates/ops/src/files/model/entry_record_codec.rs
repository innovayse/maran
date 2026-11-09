//! The byte format in which a dropped child hands a listing to the root parent.

use super::entry_kind::EntryKind;
use super::entry_record_error::EntryRecordError;
use super::listed_entries::{ListedEntries, MAXIMUM_LISTED_ENTRIES};
use super::listed_entry::ListedEntry;
use super::listing_answer::ListingAnswer;
use super::record_reader::RecordReader;

/// Answer tag: a listing follows.
const TAG_ENTRIES: u8 = 0;
/// Answer tag: the directory was too large; nothing follows.
const TAG_TOO_LARGE: u8 = 1;

/// Fewest bytes one entry takes (a one-byte name), used to bound an allocation
/// by the bytes actually present rather than by the count the child announced.
const MINIMUM_ENTRY_BYTES: usize = 40;

/// Longest entry name, in bytes.
const MAXIMUM_NAME: usize = 255;

/// Encodes `answer`: little-endian, length-prefixed, nothing implicit.
///
/// Layout: tag; for a listing, `u32` skipped, `u32` count, then per entry
/// `u16` name length, name, kind byte, `u64` size, `u32` mode, `i64` modified,
/// `u32` uid, `u32` gid, `u64` link count.
#[must_use]
pub(crate) fn encode_listing(answer: &ListingAnswer) -> Vec<u8> {
    let mut out = Vec::new();
    match answer {
        ListingAnswer::TooLarge => out.push(TAG_TOO_LARGE),
        ListingAnswer::Entries(listed) => {
            out.push(TAG_ENTRIES);
            out.extend_from_slice(&listed.skipped.to_le_bytes());
            let count = u32::try_from(listed.entries.len()).unwrap_or(u32::MAX);
            out.extend_from_slice(&count.to_le_bytes());
            for entry in &listed.entries {
                encode_entry(&mut out, entry);
            }
        }
    }
    out
}

/// Appends one entry to `out`. Shared with every answer that carries entries.
pub(crate) fn encode_entry(out: &mut Vec<u8>, entry: &ListedEntry) {
    let name = entry.name.as_bytes();
    let length = u16::try_from(name.len()).unwrap_or(u16::MAX);
    out.extend_from_slice(&length.to_le_bytes());
    out.extend_from_slice(name);
    out.push(kind_tag(entry.kind));
    out.extend_from_slice(&entry.size.to_le_bytes());
    out.extend_from_slice(&entry.mode.to_le_bytes());
    out.extend_from_slice(&entry.modified_unix.to_le_bytes());
    out.extend_from_slice(&entry.uid.to_le_bytes());
    out.extend_from_slice(&entry.gid.to_le_bytes());
    out.extend_from_slice(&entry.link_count.to_le_bytes());
}

/// Decodes a listing answer, strictly: any malformed byte is an error and no
/// partial listing is ever returned.
///
/// # Errors
///
/// Returns the [`EntryRecordError`] naming what was wrong: truncation, a count
/// above [`MAXIMUM_LISTED_ENTRIES`], an over-long or non-UTF-8 name, an unknown
/// tag, or trailing bytes.
pub(crate) fn decode_listing(bytes: &[u8]) -> Result<ListingAnswer, EntryRecordError> {
    let mut reader = RecordReader::new(bytes);
    let answer = match reader.u8()? {
        TAG_TOO_LARGE => ListingAnswer::TooLarge,
        TAG_ENTRIES => {
            let skipped = reader.u32()?;
            let count = usize::try_from(reader.u32()?).unwrap_or(usize::MAX);
            if count > MAXIMUM_LISTED_ENTRIES {
                return Err(EntryRecordError::TooManyEntries);
            }
            // Capacity from the bytes present, not from the announced count.
            let mut entries =
                Vec::with_capacity(count.min(reader.remaining() / MINIMUM_ENTRY_BYTES));
            for _ in 0..count {
                entries.push(decode_entry(&mut reader)?);
            }
            ListingAnswer::Entries(ListedEntries { entries, skipped })
        }
        _ => return Err(EntryRecordError::UnknownTag),
    };
    reader.finish()?;
    Ok(answer)
}

/// Reads one entry from `reader`.
///
/// # Errors
///
/// As [`decode_listing`], for the entry alone.
pub(crate) fn decode_entry(reader: &mut RecordReader<'_>) -> Result<ListedEntry, EntryRecordError> {
    let length = usize::from(reader.u16()?);
    if length > MAXIMUM_NAME {
        return Err(EntryRecordError::NameTooLong);
    }
    let name = std::str::from_utf8(reader.take(length)?)
        .map_err(|_| EntryRecordError::NotUtf8)?
        .to_owned();
    let kind = kind_from_tag(reader.u8()?)?;
    Ok(ListedEntry {
        name,
        kind,
        size: reader.u64()?,
        mode: reader.u32()?,
        modified_unix: reader.i64()?,
        uid: reader.u32()?,
        gid: reader.u32()?,
        link_count: reader.u64()?,
    })
}

/// The byte an entry kind travels as.
fn kind_tag(kind: EntryKind) -> u8 {
    match kind {
        EntryKind::File => 0,
        EntryKind::Directory => 1,
        EntryKind::Symlink => 2,
        EntryKind::Other => 3,
    }
}

/// The kind a byte stands for.
fn kind_from_tag(tag: u8) -> Result<EntryKind, EntryRecordError> {
    match tag {
        0 => Ok(EntryKind::File),
        1 => Ok(EntryKind::Directory),
        2 => Ok(EntryKind::Symlink),
        3 => Ok(EntryKind::Other),
        _ => Err(EntryRecordError::UnknownTag),
    }
}

#[cfg(test)]
#[path = "../../tests/files/entry_record_codec_tests.rs"]
mod tests;
