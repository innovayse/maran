//! Turning a trash listing into the bytes the child sends the parent.

use crate::files::model::trashed_entry::TrashedEntry;

/// Writes `entries` as `u32 count` then, per entry, `u16 length + id`,
/// `u16 length + original path`, `i64 trashed-at`, `u8 is-directory` and
/// `u64 size`, all little-endian.
///
/// Lengths are written as `u16` and a string longer than that is cut at the
/// type's limit, which no real value reaches: an identifier is at most 40 bytes
/// and an original path at most 4096. The decoder is the strict half; what this
/// writes is only ever read back by `decode_trash_listing`.
#[must_use]
pub(crate) fn encode_trash_listing(entries: &[TrashedEntry]) -> Vec<u8> {
    let mut out = Vec::new();
    let count = u32::try_from(entries.len()).unwrap_or(u32::MAX);
    out.extend_from_slice(&count.to_le_bytes());
    for entry in entries {
        push_text(&mut out, &entry.trash_id);
        push_text(&mut out, &entry.original_path);
        out.extend_from_slice(&entry.trashed_at_unix.to_le_bytes());
        out.push(u8::from(entry.is_directory));
        out.extend_from_slice(&entry.size_bytes.to_le_bytes());
    }
    out
}

/// Appends a `u16` length and the text's bytes.
fn push_text(out: &mut Vec<u8>, text: &str) {
    let bytes = text.as_bytes();
    let length = u16::try_from(bytes.len()).unwrap_or(u16::MAX);
    out.extend_from_slice(&length.to_le_bytes());
    out.extend_from_slice(&bytes[..usize::from(length)]);
}

#[cfg(test)]
#[path = "../tests/files/encode_trash_listing_tests.rs"]
mod tests;
