//! Reading a trash listing the child sent, distrusting every byte.

use crate::files::list_trash_in_home::MAXIMUM_LISTED_TRASH_ENTRIES;
use crate::files::model::entry_record_error::EntryRecordError;
use crate::files::model::record_reader::RecordReader;
use crate::files::model::trashed_entry::TrashedEntry;

/// Smallest an encoded entry can be: two empty strings and the fixed fields.
const MINIMUM_ENTRY_BYTES: usize = 2 + 2 + 8 + 1 + 8;

/// Decodes what `encode_trash_listing` wrote.
///
/// The bytes came from a process that was the customer a moment ago, so they are
/// parsed strictly and bounded: an announced count above the listing ceiling is
/// refused, the vector's capacity comes from the bytes actually present and not
/// from the announced count, text must be UTF-8, a boolean must be 0 or 1, and
/// trailing bytes are an error. Any failure is the child failing to produce an
/// answer and never a partial listing.
///
/// # Errors
///
/// Returns an [`EntryRecordError`] for a truncated answer, too many entries,
/// text that is not UTF-8, a boolean that is neither 0 nor 1, or trailing bytes.
pub(crate) fn decode_trash_listing(bytes: &[u8]) -> Result<Vec<TrashedEntry>, EntryRecordError> {
    let mut reader = RecordReader::new(bytes);
    let count = usize::try_from(reader.u32()?).unwrap_or(usize::MAX);
    if count > MAXIMUM_LISTED_TRASH_ENTRIES {
        return Err(EntryRecordError::TooManyEntries);
    }

    let mut entries = Vec::with_capacity(count.min(reader.remaining() / MINIMUM_ENTRY_BYTES));
    for _ in 0..count {
        let trash_id = read_text(&mut reader)?;
        let original_path = read_text(&mut reader)?;
        let trashed_at_unix = reader.i64()?;
        let is_directory = match reader.u8()? {
            0 => false,
            1 => true,
            _ => return Err(EntryRecordError::UnknownTag),
        };
        let size_bytes = reader.u64()?;
        entries.push(TrashedEntry {
            trash_id,
            original_path,
            trashed_at_unix,
            is_directory,
            size_bytes,
        });
    }
    reader.finish()?;

    Ok(entries)
}

/// Reads a `u16` length and that many bytes as UTF-8.
fn read_text(reader: &mut RecordReader<'_>) -> Result<String, EntryRecordError> {
    let length = usize::from(reader.u16()?);
    std::str::from_utf8(reader.take(length)?)
        .map(str::to_owned)
        .map_err(|_| EntryRecordError::NotUtf8)
}

#[cfg(test)]
#[path = "../tests/files/decode_trash_listing_tests.rs"]
mod tests;
