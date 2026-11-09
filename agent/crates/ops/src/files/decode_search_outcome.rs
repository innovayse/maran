//! Reading back the bytes a search child sent.

use maran_agent_core::privs::entry_kind::EntryKind;

use crate::files::model::entry_record_error::EntryRecordError;
use crate::files::model::record_reader::RecordReader;
use crate::files::model::search_match::SearchMatch;
use crate::files::model::search_outcome::SearchOutcome;

/// Most matches an encoded outcome may declare; the search never returns more.
const MAXIMUM_MATCHES: usize = 500;

/// Longest path an encoded match may declare, in bytes.
///
/// 64 start components plus 16 levels of 255 bytes and separators is under
/// this; anything longer is not something the encoder produces.
const MAXIMUM_PATH_BYTES: usize = 32 * 1024;

/// Decodes what [`encode_search_outcome`](super::encode_search_outcome::encode_search_outcome) wrote.
///
/// The bytes come from a process that ran as a customer. Every count and length
/// is checked against its bound and against the bytes that remain BEFORE
/// anything is allocated for it, and bytes left over are refused. A failure is
/// an error and never a partial result.
///
/// # Errors
///
/// Returns [`EntryRecordError::Truncated`] when the bytes end early,
/// [`EntryRecordError::TooManyEntries`] for a count above 500,
/// [`EntryRecordError::NameTooLong`] for a path above its bound,
/// [`EntryRecordError::NotUtf8`] for a path that is not UTF-8,
/// [`EntryRecordError::UnknownTag`] for a flag that is not 0 or 1 or an unknown
/// kind, and [`EntryRecordError::TrailingBytes`] for bytes after the last match.
pub(crate) fn decode_search_outcome(bytes: &[u8]) -> Result<SearchOutcome, EntryRecordError> {
    let mut reader = RecordReader::new(bytes);

    let truncated = match reader.u8()? {
        0 => false,
        1 => true,
        _ => return Err(EntryRecordError::UnknownTag),
    };
    let skipped_entries = reader.u32()?;
    let count = reader.u32()? as usize;
    if count > MAXIMUM_MATCHES {
        return Err(EntryRecordError::TooManyEntries);
    }

    let mut matches = Vec::with_capacity(count);
    for _ in 0..count {
        let length = reader.u32()? as usize;
        if length > MAXIMUM_PATH_BYTES {
            return Err(EntryRecordError::NameTooLong);
        }
        let path = std::str::from_utf8(reader.take(length)?)
            .map_err(|_| EntryRecordError::NotUtf8)?
            .to_owned();
        let kind = match reader.u8()? {
            0 => EntryKind::File,
            1 => EntryKind::Directory,
            2 => EntryKind::Symlink,
            3 => EntryKind::Other,
            _ => return Err(EntryRecordError::UnknownTag),
        };

        matches.push(SearchMatch {
            path,
            kind,
            size: reader.u64()?,
            mode: reader.u32()?,
            modified_unix: reader.i64()?,
            uid: reader.u32()?,
            gid: reader.u32()?,
            links: reader.u64()?,
        });
    }
    reader.finish()?;

    Ok(SearchOutcome {
        matches,
        truncated,
        skipped_entries,
    })
}

#[cfg(test)]
#[path = "../tests/files/decode_search_outcome_tests.rs"]
mod tests;
