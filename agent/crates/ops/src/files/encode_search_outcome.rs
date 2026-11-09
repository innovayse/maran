//! Turning a search outcome into the bytes the child sends to the parent.

use maran_agent_core::privs::entry_kind::EntryKind;

use crate::files::model::search_outcome::SearchOutcome;

/// The byte for an [`EntryKind`] on the wire between child and parent.
fn kind_byte(kind: EntryKind) -> u8 {
    match kind {
        EntryKind::File => 0,
        EntryKind::Directory => 1,
        EntryKind::Symlink => 2,
        EntryKind::Other => 3,
    }
}

/// Encodes `outcome` as little-endian, length-prefixed fields.
///
/// Layout: `truncated` (1 byte), `skipped_entries` (u32), match count (u32),
/// then per match the path (u32 length and bytes), the kind (1 byte), size
/// (u64), mode (u32), modified (i64), uid (u32), gid (u32) and links (u64). The
/// parent decodes with [`decode_search_outcome`](super::decode_search_outcome::decode_search_outcome),
/// which treats every length as hostile.
#[must_use]
pub(crate) fn encode_search_outcome(outcome: &SearchOutcome) -> Vec<u8> {
    let mut bytes = Vec::new();
    bytes.push(u8::from(outcome.truncated));
    bytes.extend_from_slice(&outcome.skipped_entries.to_le_bytes());
    let count = u32::try_from(outcome.matches.len()).unwrap_or(u32::MAX);
    bytes.extend_from_slice(&count.to_le_bytes());

    for found in &outcome.matches {
        let path = found.path.as_bytes();
        let length = u32::try_from(path.len()).unwrap_or(u32::MAX);
        bytes.extend_from_slice(&length.to_le_bytes());
        bytes.extend_from_slice(path);
        bytes.push(kind_byte(found.kind));
        bytes.extend_from_slice(&found.size.to_le_bytes());
        bytes.extend_from_slice(&found.mode.to_le_bytes());
        bytes.extend_from_slice(&found.modified_unix.to_le_bytes());
        bytes.extend_from_slice(&found.uid.to_le_bytes());
        bytes.extend_from_slice(&found.gid.to_le_bytes());
        bytes.extend_from_slice(&found.links.to_le_bytes());
    }

    bytes
}

#[cfg(test)]
#[path = "../tests/files/encode_search_outcome_tests.rs"]
mod tests;
